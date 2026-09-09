using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Reporting;

internal enum ReportSampleGrouping { Summary, Monitor, Day }

internal sealed class RetainedReportSamples(ApplicationDbContext database)
{
    internal const string SourcesSql = """
        WITH archived_days AS (
            SELECT aggregate.*,
                aggregate.utc_date::timestamp AT TIME ZONE 'UTC' AS day_start
            FROM web_health.monitoring_daily_aggregate aggregate
            WHERE aggregate.endpoint_monitor_id = ANY(@monitor_ids)
                AND (aggregate.raw_deletion_started_at IS NOT NULL OR aggregate.exact_duration_samples IS NOT NULL)
                AND aggregate.utc_date >= (@window_start AT TIME ZONE 'UTC')::date
                AND aggregate.utc_date <= (@window_end AT TIME ZONE 'UTC')::date
                AND aggregate.utc_date::timestamp AT TIME ZONE 'UTC' < @window_end
        ), covered_days AS (
            SELECT * FROM archived_days
            WHERE day_start >= @window_start AND day_start + interval '24 hours' <= @window_end
        ), raw_ranges AS (
            SELECT monitor.endpoint_monitor_id, day.value::date AS utc_date,
                greatest(day.value::date::timestamp AT TIME ZONE 'UTC', @window_start) AS range_start,
                least((day.value::date + 1)::timestamp AT TIME ZONE 'UTC', @window_end) AS range_end
            FROM unnest(@monitor_ids) AS monitor(endpoint_monitor_id)
            CROSS JOIN generate_series((@window_start AT TIME ZONE 'UTC')::date,
                ((@window_end - interval '1 microsecond') AT TIME ZONE 'UTC')::date, interval '1 day') AS day(value)
            WHERE NOT EXISTS (SELECT 1 FROM archived_days archive
                WHERE archive.endpoint_monitor_id = monitor.endpoint_monitor_id
                    AND archive.utc_date = day.value::date)
        ), raw_results AS (
            SELECT result.* FROM raw_ranges range
            JOIN web_health.check_result result ON result.endpoint_monitor_id = range.endpoint_monitor_id
                AND result.measured_at >= range.range_start AND result.measured_at < range.range_end
        )
        """;

    private const string Responded = "result.counts_for_uptime AND result.outcome IN ('Healthy', 'Warning')";
    private const string Available = "(result.failure_category IS NULL OR result.failure_category = ANY(@non_availability_categories))";

    public async Task<Dictionary<string, ReportSampleAggregate>> LoadAsync(ReportQuery query, IReadOnlyList<Guid> monitorIds,
        ReportSampleGrouping grouping, CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0) return [];
        var rawKey = grouping switch
        {
            ReportSampleGrouping.Monitor => "result.endpoint_monitor_id::text",
            ReportSampleGrouping.Day => "((result.measured_at AT TIME ZONE 'UTC')::date)::text",
            _ => "''::text"
        };
        var dailyKey = grouping switch
        {
            ReportSampleGrouping.Monitor => "endpoint_monitor_id::text",
            ReportSampleGrouping.Day => "utc_date::text",
            _ => "''::text"
        };
        var rawGroup = grouping switch
        {
            ReportSampleGrouping.Monitor => "GROUP BY result.endpoint_monitor_id",
            ReportSampleGrouping.Day => "GROUP BY (result.measured_at AT TIME ZONE 'UTC')::date",
            _ => string.Empty
        };
        var dailyGroup = grouping switch
        {
            ReportSampleGrouping.Monitor => "GROUP BY endpoint_monitor_id",
            ReportSampleGrouping.Day => "GROUP BY utc_date",
            _ => string.Empty
        };
        var rawHistogram = string.Join(", ", Enumerable.Range(0, ResponseTimeHistogram.BucketCount).Select(index =>
        {
            var lower = index == 0 ? string.Empty : $" AND result.total_duration_ms > {ResponseTimeHistogram.UpperBoundsMs[index - 1]}";
            var upper = index == ResponseTimeHistogram.UpperBoundsMs.Count ? string.Empty : $" AND result.total_duration_ms <= {ResponseTimeHistogram.UpperBoundsMs[index]}";
            return $"count(*) FILTER (WHERE EXISTS (SELECT 1 FROM covered_days WHERE duration_count > 0) AND {Responded}{lower}{upper})";
        }));
        var dailyHistogram = string.Join(", ", Enumerable.Range(1, ResponseTimeHistogram.BucketCount)
            .Select(index => $"coalesce(sum(duration_histogram[{index}]), 0)::bigint"));
        var sql = $"""
            {SourcesSql}
            SELECT {rawKey} AS sample_key,
                count(*) FILTER (WHERE result.counts_for_uptime),
                count(*) FILTER (WHERE result.counts_for_uptime AND result.outcome = 'Healthy'),
                count(*) FILTER (WHERE result.counts_for_uptime AND {Available} AND result.outcome <> 'Healthy'),
                count(*) FILTER (WHERE result.counts_for_uptime AND NOT {Available}),
                count(*) FILTER (WHERE NOT result.counts_for_uptime),
                count(*) FILTER (WHERE {Responded}),
                NULL::double precision, NULL::double precision,
                max(result.measured_at), min(result.monitor_source), max(result.monitor_source),
                ARRAY[{rawHistogram}], max(result.total_duration_ms) FILTER (WHERE {Responded}),
                count(*), 0::bigint, false, false
            FROM raw_results result {rawGroup}
            UNION ALL
            SELECT {dailyKey}, coalesce(sum(eligible_count), 0)::bigint, coalesce(sum(healthy_count), 0)::bigint,
                coalesce(sum(warning_count), 0)::bigint, coalesce(sum(down_count), 0)::bigint,
                coalesce(sum(excluded_count), 0)::bigint, coalesce(sum(duration_count), 0)::bigint,
                NULL::double precision, NULL::double precision, max(last_measured_at), min(lowest_source), max(highest_source),
                ARRAY[{dailyHistogram}], max(duration_maximum_ms), 0::bigint, coalesce(sum(total_count), 0)::bigint,
                false, coalesce(bool_or(exact_duration_samples IS NULL AND duration_count > 0), false)
            FROM covered_days {dailyGroup}
            UNION ALL
            SELECT {dailyKey}, 0::bigint, 0::bigint, 0::bigint, 0::bigint, 0::bigint, 0::bigint,
                NULL::double precision, NULL::double precision, NULL::timestamptz, NULL::text, NULL::text,
                array_fill(0::bigint, ARRAY[{ResponseTimeHistogram.BucketCount}]), NULL::integer, 0::bigint, 0::bigint, true,
                false
            FROM archived_days WHERE day_start < @window_start OR day_start + interval '24 hours' > @window_end
            {(grouping == ReportSampleGrouping.Summary ? "GROUP BY 1" : dailyGroup)};
            """;
        var samples = new Dictionary<string, ReportSampleAggregate>(StringComparer.Ordinal);
        await using (var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken))
        await using (var reader = await scope.Command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.GetString(0);
                if (!samples.TryGetValue(key, out var sample)) samples.Add(key, sample = new());
                sample.Add(reader);
            }
        }
        await LoadExactDurationsAsync(query, monitorIds, grouping, samples, cancellationToken);
        return samples;
    }

    private async Task LoadExactDurationsAsync(ReportQuery query, IReadOnlyList<Guid> monitorIds,
        ReportSampleGrouping grouping, Dictionary<string, ReportSampleAggregate> samples, CancellationToken cancellationToken)
    {
        var sql = SourcesSql + """

            SELECT result.endpoint_monitor_id, (result.measured_at AT TIME ZONE 'UTC')::date,
                array_agg(result.total_duration_ms)
            FROM raw_results result
            WHERE result.counts_for_uptime AND result.outcome IN ('Healthy', 'Warning')
            GROUP BY 1, 2
            UNION ALL
            SELECT endpoint_monitor_id, utc_date, exact_duration_samples
            FROM covered_days WHERE exact_duration_samples IS NOT NULL;
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = grouping switch
            {
                ReportSampleGrouping.Monitor => reader.GetGuid(0).ToString(),
                ReportSampleGrouping.Day => reader.GetFieldValue<DateOnly>(1).ToString("yyyy-MM-dd"),
                _ => string.Empty
            };
            if (samples.TryGetValue(key, out var sample)) sample.AddExactDurations(reader.GetFieldValue<int[]>(2));
        }
    }

    public async Task<ComparabilityAssessment> AssessComparabilityAsync(ReportQuery query, IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0) return PerformanceComparability.Evaluate([], false);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var rawIdentities = new Dictionary<Guid, string>();
        var changed = false;
        var rawSql = SourcesSql + """
            , raw_identities AS MATERIALIZED (
            SELECT result.endpoint_monitor_id, result.configuration_identity, result.monitor_source
            FROM raw_results result
            WHERE result.counts_for_uptime GROUP BY 1, 2, 3
            )
            SELECT * FROM raw_identities ORDER BY 1, 2, 3;
            """;
        await using (var scope = await CreateCommandAsync(rawSql, query, monitorIds, cancellationToken))
        await using (var reader = await scope.Command.ExecuteReaderAsync(cancellationToken))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Guid? currentMonitor = null;
            string? previous = null;
            var identityCount = 0;
            void CompleteMonitor()
            {
                if (currentMonitor is null) return;
                rawIdentities[currentMonitor.Value] = Convert.ToHexStringLower(hash.GetHashAndReset());
                changed |= identityCount > 1;
            }
            while (await reader.ReadAsync(cancellationToken))
            {
                var monitorId = reader.GetGuid(0);
                if (monitorId != currentMonitor)
                {
                    CompleteMonitor();
                    currentMonitor = monitorId;
                    previous = null;
                    identityCount = 0;
                }
                var identity = reader.GetString(1);
                if (identity != previous)
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(identity));
                    identityCount++;
                    previous = identity;
                }
                sources.Add(reader.GetString(2));
            }
            CompleteMonitor();
        }
        var dailySql = SourcesSql + """

            SELECT endpoint_monitor_id, min(comparability_identity), max(comparability_identity), bool_and(is_comparable),
                min(lowest_source), max(highest_source)
            FROM covered_days WHERE eligible_count > 0 GROUP BY endpoint_monitor_id;
            """;
        await using (var scope = await CreateCommandAsync(dailySql, query, monitorIds, cancellationToken))
        await using (var reader = await scope.Command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var identity = reader.GetString(1);
                changed |= !reader.GetBoolean(3) || identity != reader.GetString(2)
                    || (rawIdentities.TryGetValue(reader.GetGuid(0), out var rawIdentity) && rawIdentity != identity);
                sources.Add(reader.GetString(4));
                sources.Add(reader.GetString(5));
            }
        }
        return PerformanceComparability.Evaluate(sources, changed);
    }

    private async Task<CommandScope> CreateCommandAsync(string sql, ReportQuery query, IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        var wasClosed = database.Database.GetDbConnection().State != ConnectionState.Open;
        if (wasClosed) await database.Database.OpenConnectionAsync(cancellationToken);
        var command = new NpgsqlCommand(sql, (NpgsqlConnection)database.Database.GetDbConnection(),
            database.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        command.Parameters.AddWithValue("monitor_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, monitorIds.ToArray());
        command.Parameters.AddWithValue("window_start", NpgsqlDbType.TimestampTz, query.WindowStart);
        command.Parameters.AddWithValue("window_end", NpgsqlDbType.TimestampTz, query.WindowEnd);
        command.Parameters.AddWithValue("non_availability_categories", NpgsqlDbType.Array | NpgsqlDbType.Text,
            UptimeParticipation.NonAvailabilityCategories.ToArray());
        return new(command, wasClosed ? database : null);
    }

    internal sealed record CommandScope(NpgsqlCommand Command, ApplicationDbContext? ContextToClose) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Command.DisposeAsync();
            if (ContextToClose is not null) await ContextToClose.Database.CloseConnectionAsync();
        }
    }
}

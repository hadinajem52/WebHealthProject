using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Reporting;

internal sealed class ReportingReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    OwnerSubjectNames ownerNames,
    TimeProvider timeProvider) : IReportingReader
{
    private const int AttentionListCount = 8;

    private static readonly TimeSpan OverdueGrace = TimeSpan.FromMinutes(10);

    private readonly Dictionary<SelectionKey, Task<Selection>> selections = [];

    public async Task<ReportDataset> QueryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var totalCount = selection.TotalCount;

        var effectiveQuery = query.WithPaging(EffectivePage(query, totalCount));
        var page = await PageAsync(selection.Monitors, effectiveQuery, cancellationToken);
        var monitorIds = selection.MonitorIds;

        return new(
            effectiveQuery,
            await BuildSummaryAsync(effectiveQuery, monitorIds, page, totalCount, cancellationToken),
            await BuildRowsAsync(effectiveQuery, page, cancellationToken),
            await BuildTrendAsync(effectiveQuery, monitorIds, cancellationToken),
            totalCount);
    }

    public async Task<ReportExport> ExportAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var exportQuery = query.ForExport();
        var selection = await ResolveSelectionAsync(exportQuery, access, cancellationToken);
        var page = await PageAsync(selection.Monitors, exportQuery, cancellationToken);
        return new(
            exportQuery,
            await BuildRowsAsync(exportQuery, page, cancellationToken),
            selection.TotalCount);
    }

    public async Task<ReportCertificateExpiry> QueryCertificateExpiryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var totalCount = selection.TotalCount;

        var certificateMonitors = await SelectMonitors(
                query, access, selection.Now, SslMonitorIdentity.MonitorType)
            .ToArrayAsync(cancellationToken);
        var notApplicable = totalCount - certificateMonitors.Length;
        if (certificateMonitors.Length == 0)
        {
            return ReportCertificateExpiry.Empty with { NotApplicableCount = notApplicable };
        }

        var monitorIds = certificateMonitors.Select(monitor => monitor.EndpointMonitorId).ToArray();
        var latest = await dbContext.CertificateObservations.AsNoTracking()
            .Where(observation => monitorIds.Contains(observation.EndpointMonitorId))
            .GroupBy(observation => observation.EndpointMonitorId)
            .Select(group => group
                .OrderByDescending(observation => observation.ObservedAt)
                .ThenByDescending(observation => observation.LogicalCheckId)
                .First())
            .ToArrayAsync(cancellationToken);
        var byMonitor = latest.ToDictionary(observation => observation.EndpointMonitorId);

        var items = certificateMonitors
            .Where(monitor => byMonitor.ContainsKey(monitor.EndpointMonitorId))
            .Select(monitor =>
            {
                var observation = byMonitor[monitor.EndpointMonitorId];
                var isValid = observation.ValidationCategory == nameof(TlsValidationCategory.Valid);
                return new CertificateExpiryItem(
                    monitor.EndpointId,
                    monitor.EndpointDisplayUrl,
                    monitor.ClientName,
                    monitor.EnvironmentName,
                    observation.NotAfter,
                    observation.DaysRemaining,
                    observation.ValidationCategory,
                    isValid,
                    SelectExpirySeverity(observation.ValidationCategory, observation.DaysRemaining),
                    observation.ObservedAt);
            })
            .ToArray();

        return new(
            notApplicable,
            certificateMonitors.Length - items.Length,
            items.Count(item => item.IsValid && item.Severity == CertificateExpirySeverity.None),
            items.Count(item => !item.IsValid),
            items.Count(item => item.Severity == CertificateExpirySeverity.Warning),
            items.Count(item => item.Severity == CertificateExpirySeverity.High),
            items.Count(item => item.Severity == CertificateExpirySeverity.Critical),
            items
                .Where(item => !item.IsValid || item.Severity != CertificateExpirySeverity.None)
                .OrderByDescending(item => !item.IsValid)
                .ThenBy(item => item.DaysRemaining)
                .ThenBy(item => item.EndpointDisplayUrl, StringComparer.Ordinal)
                .Take(AttentionListCount)
                .ToArray());
    }

    private static CertificateExpirySeverity SelectExpirySeverity(
        string validationCategory,
        int daysRemaining)
    {
        if (validationCategory == nameof(TlsValidationCategory.Expired))
        {
            return CertificateExpirySeverity.Critical;
        }

        return validationCategory == nameof(TlsValidationCategory.Valid)
            ? CertificateExpiry.SelectSeverity(daysRemaining, CertificateExpiryThresholds.Default)
            : CertificateExpirySeverity.None;
    }

    public async Task<ReportDiagnostics> QueryDiagnosticsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var monitorIds = selection.MonitorIds;
        if (monitorIds.Count == 0)
        {
            return ReportDiagnostics.Empty;
        }

        var overdueBefore = selection.Now - OverdueGrace;
        var scheduling = await dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .Select(monitor => new
            {
                LifecycleEnabled = monitor.Endpoint.IsEnabled
                    && monitor.Endpoint.DeletedAt == null
                    && monitor.Endpoint.Environment.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.IsEnabled
                    && monitor.Endpoint.Environment.Website.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.Client.IsActive
                    && monitor.Endpoint.Environment.Website.Client.DeletedAt == null,
                monitor.SchedulingEnabled,
                monitor.IsEnabled,
                monitor.NextDueAt
            })
            .ToArrayAsync(cancellationToken);

        var work = await dbContext.DurableWork.AsNoTracking()
            .Where(item => monitorIds.Contains(item.LogicalCheck.EndpointMonitorId))
            .GroupBy(item => item.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var inFlight = work
            .Where(item => item.State == DurableWorkStates.Pending
                || item.State == DurableWorkStates.Dispatching
                || item.State == DurableWorkStates.Enqueued
                || item.State == DurableWorkStates.Processing)
            .Sum(item => item.Count);

        var lastCompleted = await dbContext.CheckResults.AsNoTracking()
            .Where(result => monitorIds.Contains(result.EndpointMonitorId))
            .OrderByDescending(result => result.MeasuredAt)
            .Select(result => (DateTimeOffset?)result.MeasuredAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new(
            scheduling.Count(monitor => monitor.LifecycleEnabled
                && monitor.SchedulingEnabled && monitor.IsEnabled),
            scheduling.Count(monitor => !monitor.LifecycleEnabled
                || monitor.SchedulingEnabled && !monitor.IsEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled && !monitor.SchedulingEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled
                && monitor.SchedulingEnabled && monitor.IsEnabled
                && monitor.NextDueAt < overdueBefore),
            inFlight,
            work.Where(item => item.State == DurableWorkStates.Failed).Sum(item => item.Count),
            lastCompleted);
    }

    public async Task<IReadOnlyList<ReportIncidentItem>> QueryActiveIncidentsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var monitorIds = selection.MonitorIds;
        if (monitorIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.Incidents.AsNoTracking()
            .Where(incident => monitorIds.Contains(incident.EndpointMonitorId)
                && IncidentStatuses.Active.Contains(incident.Status))
            .OrderByDescending(incident => incident.OpenedAt)
            .ThenBy(incident => incident.Id)
            .Take(Math.Max(limit, 1))
            .Select(incident => new
            {
                incident.Id,
                incident.EndpointMonitor.EndpointId,
                incident.EndpointMonitor.Endpoint.DisplayUrl,
                ClientName = incident.EndpointMonitor.Endpoint.Environment.Website.Client.Name,
                EnvironmentName = incident.EndpointMonitor.Endpoint.Environment.Name,
                incident.IssueKey,
                incident.EndpointMonitor.MonitorType,
                incident.Severity,
                incident.Status,
                incident.OpenedAt,
                incident.AcknowledgedAt,
                incident.OwnerSubjectId
            })
            .ToArrayAsync(cancellationToken);

        var names = await ownerNames.LoadAsync(rows.Select(row => row.OwnerSubjectId), cancellationToken);
        return rows.Select(row => new ReportIncidentItem(
            row.Id,
            row.EndpointId,
            row.DisplayUrl,
            row.ClientName,
            row.EnvironmentName,
            row.IssueKey,
            row.MonitorType,
            row.Severity,
            row.Status,
            row.OpenedAt,
            row.AcknowledgedAt,
            names.GetValueOrDefault(row.OwnerSubjectId, "Unknown owner"))).ToArray();
    }

    private IQueryable<MonitorRow> SelectMonitors(
        ReportQuery query,
        RegistryAccessContext access,
        DateTimeOffset now,
        string? restrictToMonitorType = null)
    {
        var endpoints = visibility
            .ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Where(endpoint => endpoint.DeletedAt == null);

        if (query.ClientId is { } clientId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.Environment.Website.ClientId == clientId);
        }

        if (query.WebsiteId is { } websiteId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.Environment.WebsiteId == websiteId);
        }

        if (query.EnvironmentId is { } environmentId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.EnvironmentId == environmentId);
        }

        if (query.OwnerSubjectId is { } ownerSubjectId)
        {
            endpoints = endpoints.Where(endpoint =>
                (endpoint.OwnerSubjectId ?? endpoint.Environment.Website.OwnerSubjectId) == ownerSubjectId);
        }

        var monitors = endpoints
            .SelectMany(endpoint => endpoint.Monitors.Where(monitor => monitor.DeletedAt == null));

        if (query.MonitorType is { } monitorType)
        {
            monitors = monitors.Where(monitor => monitor.MonitorType == monitorType);
        }

        if (restrictToMonitorType is { } restriction)
        {
            monitors = monitors.Where(monitor => monitor.MonitorType == restriction);
        }

        if (query.HealthStatus is { } healthStatus)
        {
            monitors = monitors.Where(MonitorDisplayStatus.Matches(healthStatus));
        }

        var projected = monitors
            .OrderBy(monitor => monitor.Endpoint.Environment.Website.Client.Name)
            .ThenBy(monitor => monitor.Endpoint.Environment.Website.Name)
            .ThenBy(monitor => monitor.Endpoint.Environment.Name)
            .ThenBy(monitor => monitor.Endpoint.DisplayUrl)
            .ThenBy(monitor => monitor.MonitorType)
            .ThenBy(monitor => monitor.Id)
            .Select(monitor => new MonitorRow(
                monitor.Id,
                monitor.EndpointId,
                monitor.Endpoint.Environment.Website.Client.Name,
                monitor.Endpoint.Environment.Website.Name,
                monitor.Endpoint.Environment.Name,
                monitor.Endpoint.Environment.IsProduction,
                monitor.Endpoint.DisplayUrl,
                monitor.MonitorType,
                monitor.Endpoint.OwnerSubjectId
                    ?? monitor.Endpoint.Environment.Website.OwnerSubjectId,
                !monitor.IsEnabled
                    || !monitor.Endpoint.IsEnabled
                    || monitor.Endpoint.DeletedAt != null
                            || monitor.Endpoint.Environment.DeletedAt != null
                    || !monitor.Endpoint.Environment.Website.IsEnabled
                    || monitor.Endpoint.Environment.Website.DeletedAt != null
                    || !monitor.Endpoint.Environment.Website.Client.IsActive
                    || monitor.Endpoint.Environment.Website.Client.DeletedAt != null
                    ? EndpointHealthStatuses.Disabled
                    : monitor.EndpointHealth == null
                        ? EndpointHealthStatuses.Unknown
                        : monitor.EndpointHealth.ConfirmedStatus,
                (monitor.IsEnabled && monitor.Endpoint.IsEnabled
                    && monitor.Endpoint.DeletedAt == null
                    && monitor.Endpoint.Environment.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.IsEnabled
                    && monitor.Endpoint.Environment.Website.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.Client.IsActive
                    && monitor.Endpoint.Environment.Website.Client.DeletedAt == null)
                    || monitor.EndpointHealth == null
                    ? null
                    : monitor.EndpointHealth.ConfirmedStatus,
                monitor.EndpointHealth == null
                    ? null
                    : (DateTimeOffset?)monitor.EndpointHealth.ConfirmedAt,
                dbContext.Incidents.Count(incident =>
                    incident.EndpointMonitorId == monitor.Id
                    && IncidentStatuses.Active.Contains(incident.Status))));

        return projected;
    }

    private Task<Selection> ResolveSelectionAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var key = SelectionKey.For(query, access);
        if (selections.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var resolving = ResolveAsync();
        selections[key] = resolving;
        return resolving;

        async Task<Selection> ResolveAsync()
        {
            var now = timeProvider.GetUtcNow();
            var monitors = SelectMonitors(query, access, now);
            var totalCount = await monitors.CountAsync(cancellationToken);
            if (totalCount > ReportQueryNormalizer.MaximumMonitors)
            {
                throw new ReportTooLargeException(ReportQueryNormalizer.MaximumMonitors);
            }

            var monitorIds = await monitors
                .Select(row => row.EndpointMonitorId)
                .Take(ReportQueryNormalizer.MaximumMonitors)
                .ToArrayAsync(cancellationToken);

            return new Selection(now, monitors, totalCount, monitorIds);
        }
    }

    private sealed record SelectionKey(ReportQuery Query, Guid UserId, string Roles)
    {
        public static SelectionKey For(ReportQuery query, RegistryAccessContext access) => new(
            query,
            access.UserId,
            string.Join("|", access.Roles.OrderBy(role => role, StringComparer.Ordinal)));
    }

    private sealed record Selection(
        DateTimeOffset Now,
        IQueryable<MonitorRow> Monitors,
        int TotalCount,
        IReadOnlyList<Guid> MonitorIds);

    private static int EffectivePage(ReportQuery query, int totalCount) =>
        Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize)));

    private static Task<MonitorRow[]> PageAsync(
        IQueryable<MonitorRow> selection,
        ReportQuery query,
        CancellationToken cancellationToken) =>
        selection
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<ReportRow>> BuildRowsAsync(
        ReportQuery query,
        IReadOnlyList<MonitorRow> monitors,
        CancellationToken cancellationToken)
    {
        if (monitors.Count == 0)
        {
            return [];
        }

        var monitorIds = monitors.Select(monitor => monitor.EndpointMonitorId).ToArray();
        var samples = await LoadSamplesAsync(query, monitorIds, groupByMonitor: true, cancellationToken);
        var names = await ownerNames.LoadAsync(
            monitors.Select(monitor => monitor.OwnerSubjectId), cancellationToken);

        return monitors.Select(monitor =>
        {
            var sample = samples.GetValueOrDefault(monitor.EndpointMonitorId) ?? SampleAggregate.Empty;
            return new ReportRow(
                monitor.EndpointMonitorId,
                monitor.EndpointId,
                monitor.ClientName,
                monitor.WebsiteName,
                monitor.EnvironmentName,
                monitor.IsProduction,
                monitor.EndpointDisplayUrl,
                monitor.MonitorType,
                names.GetValueOrDefault(monitor.OwnerSubjectId, "Unknown owner"),
                monitor.ConfirmedStatus,
                monitor.StatusBeforeDisabled,
                monitor.ConfirmedAt,
                sample.ToUptime(),
                sample.ToResponseTimes(),
                sample.LastMeasuredAt,
                monitor.ActiveIncidentCount,
                sample.SingleMonitorSource);
        }).ToArray();
    }

    private async Task<ReportSummary> BuildSummaryAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        IReadOnlyList<MonitorRow> page,
        int totalCount,
        CancellationToken cancellationToken)
    {
        var totals = monitorIds.Count == 0
            ? SampleAggregate.Empty
            : (await LoadSamplesAsync(query, monitorIds, groupByMonitor: false, cancellationToken))
                .Values.SingleOrDefault() ?? SampleAggregate.Empty;
        var health = monitorIds.Count == 0 ? [] : await LoadHealthCountsAsync(monitorIds, cancellationToken);

        return new ReportSummary(
            totalCount,
            monitorIds.Count == 0 ? 0 : await CountDistinctEndpointsAsync(monitorIds, cancellationToken),
            health.GetValueOrDefault(EndpointHealthStatuses.Healthy),
            health.GetValueOrDefault(EndpointHealthStatuses.Warning),
            health.GetValueOrDefault(EndpointHealthStatuses.Critical),
            health.GetValueOrDefault(EndpointHealthStatuses.Unknown),
            health.GetValueOrDefault(EndpointHealthStatuses.Disabled),
            monitorIds.Count == 0 ? 0 : await CountActiveIncidentsAsync(monitorIds, cancellationToken),
            totals.ToUptime(),
            totals.ToResponseTimes(),
            await AssessComparabilityAsync(query, monitorIds, cancellationToken));
    }

    private async Task<Dictionary<string, int>> LoadHealthCountsAsync(
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .GroupBy(MonitorDisplayStatus.Projection)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        return counts.ToDictionary(item => item.Status, item => item.Count, StringComparer.Ordinal);
    }

    private Task<int> CountDistinctEndpointsAsync(
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken) =>
        dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .Select(monitor => monitor.EndpointId)
            .Distinct()
            .CountAsync(cancellationToken);

    private Task<int> CountActiveIncidentsAsync(
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken) =>
        dbContext.Incidents.AsNoTracking()
            .CountAsync(
                incident => monitorIds.Contains(incident.EndpointMonitorId)
                    && IncidentStatuses.Active.Contains(incident.Status),
                cancellationToken);

    private async Task<ComparabilityAssessment> AssessComparabilityAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0)
        {
            return PerformanceComparability.Evaluate([], configurationChanged: false);
        }

        const string sql = """
            WITH per_monitor AS (
                SELECT
                    result.endpoint_monitor_id,
                    array_agg(DISTINCT result.monitor_source) AS sources,
                    min(snapshot.configuration_fingerprint)
                        <> max(snapshot.configuration_fingerprint) AS changed
                FROM web_health.check_result AS result
                JOIN web_health.check_configuration_snapshot AS snapshot
                  ON snapshot.logical_check_id = result.logical_check_id
                WHERE result.endpoint_monitor_id = ANY(@monitor_ids)
                  AND result.measured_at >= @window_start
                  AND result.measured_at < @window_end
                  AND result.counts_for_uptime
                GROUP BY result.endpoint_monitor_id
            )
            SELECT
                (SELECT array_agg(DISTINCT source)
                 FROM per_monitor, unnest(per_monitor.sources) AS source) AS monitor_sources,
                (SELECT coalesce(bool_or(changed), false) FROM per_monitor) AS configuration_changed;
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
        {
            return PerformanceComparability.Evaluate([], configurationChanged: false);
        }

        return PerformanceComparability.Evaluate(
            reader.GetFieldValue<string[]>(0),
            reader.GetBoolean(1));
    }

    private async Task<Dictionary<Guid, SampleAggregate>> LoadSamplesAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        bool groupByMonitor,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                {(groupByMonitor ? "result.endpoint_monitor_id" : "NULL::uuid")} AS monitor_id,
                count(*) FILTER (WHERE {EligibleSample}) AS eligible,
                count(*) FILTER (WHERE {HealthySample}) AS healthy_samples,
                count(*) FILTER (WHERE {WarningSample}) AS warning_samples,
                count(*) FILTER (WHERE {DownSample}) AS down_samples,
                count(*) FILTER (WHERE NOT result.counts_for_uptime) AS excluded_samples,
                count(*) FILTER (WHERE {RespondedSample}) AS responded_samples,
                percentile_cont(0.5) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p50_ms,
                percentile_cont(0.95) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p95_ms,
                max(result.measured_at) AS last_measured_at,
                min(result.monitor_source) AS lowest_source,
                max(result.monitor_source) AS highest_source
            FROM web_health.check_result AS result
            WHERE result.endpoint_monitor_id = ANY(@monitor_ids)
              AND result.measured_at >= @window_start
              AND result.measured_at < @window_end
            {(groupByMonitor ? "GROUP BY result.endpoint_monitor_id" : string.Empty)};
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        var aggregates = new Dictionary<Guid, SampleAggregate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var lowestSource = reader.IsDBNull(10) ? null : reader.GetString(10);
            var highestSource = reader.IsDBNull(11) ? null : reader.GetString(11);
            aggregates[reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0)] = new SampleAggregate(
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                string.Equals(lowestSource, highestSource, StringComparison.Ordinal) ? lowestSource : null);
        }

        return aggregates;
    }

    private const string Available =
        "(result.failure_category IS NULL "
        + "OR result.failure_category = ANY(@non_availability_categories))";

    private const string EligibleSample = "result.counts_for_uptime";

    private const string UpSample = "result.counts_for_uptime AND " + Available;

    private const string HealthySample = "result.counts_for_uptime AND result.outcome = 'Healthy'";

    private const string WarningSample =
        "result.counts_for_uptime AND " + Available + " AND result.outcome <> 'Healthy'";

    private const string DownSample = "result.counts_for_uptime AND NOT " + Available + "";

    private const string RespondedSample =
        "result.counts_for_uptime AND result.outcome IN ('Healthy', 'Warning')";

    private async Task<IReadOnlyList<ReportTrendPoint>> BuildTrendAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT
                (result.measured_at AT TIME ZONE 'UTC')::date AS day,
                count(*) FILTER (WHERE {EligibleSample}) AS eligible,
                count(*) FILTER (WHERE {UpSample}) AS up_samples,
                percentile_cont(0.5) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p50_ms,
                percentile_cont(0.95) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p95_ms
            FROM web_health.check_result AS result
            WHERE result.endpoint_monitor_id = ANY(@monitor_ids)
              AND result.measured_at >= @window_start
              AND result.measured_at < @window_end
              AND result.counts_for_uptime
            GROUP BY 1
            ORDER BY 1;
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        var points = new List<ReportTrendPoint>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var eligible = reader.GetInt64(1);
            var up = reader.GetInt64(2);
            points.Add(new(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                eligible,
                up,
                eligible == 0 ? null : Math.Round(up * 100d / eligible, 4, MidpointRounding.AwayFromZero),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4)));
        }

        return points;
    }

    private async Task<NpgsqlCommandScope> CreateCommandAsync(
        string sql,
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        var wasClosed = dbContext.Database.GetDbConnection().State != ConnectionState.Open;
        if (wasClosed)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = new NpgsqlCommand(
            sql,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        command.Parameters.AddWithValue(
            "monitor_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, monitorIds.ToArray());
        command.Parameters.AddWithValue("window_start", NpgsqlDbType.TimestampTz, query.WindowStart);
        command.Parameters.AddWithValue("window_end", NpgsqlDbType.TimestampTz, query.WindowEnd);
        command.Parameters.AddWithValue(
            "non_availability_categories",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            UptimeParticipation.NonAvailabilityCategories.ToArray());
        return new(command, wasClosed ? dbContext : null);
    }

    private sealed record NpgsqlCommandScope(NpgsqlCommand Command, ApplicationDbContext? ContextToClose)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Command.DisposeAsync();
            if (ContextToClose is not null)
            {
                await ContextToClose.Database.CloseConnectionAsync();
            }
        }
    }

    private sealed record MonitorRow(
        Guid EndpointMonitorId,
        Guid EndpointId,
        string ClientName,
        string WebsiteName,
        string EnvironmentName,
        bool IsProduction,
        string EndpointDisplayUrl,
        string MonitorType,
        Guid OwnerSubjectId,
        string ConfirmedStatus,
        string? StatusBeforeDisabled,
        DateTimeOffset? ConfirmedAt,
        int ActiveIncidentCount);

    private sealed record SampleAggregate(
        long EligibleSamples,
        long HealthySamples,
        long WarningSamples,
        long DownSamples,
        long ExcludedSamples,
        long RespondedSamples,
        double? P50Ms,
        double? P95Ms,
        DateTimeOffset? LastMeasuredAt,
        string? SingleMonitorSource)
    {
        public static SampleAggregate Empty { get; } = new(0, 0, 0, 0, 0, 0, null, null, null, null);

        public ReportUptime ToUptime() =>
            new(EligibleSamples, HealthySamples, WarningSamples, DownSamples, ExcludedSamples);

        public ReportResponseTimes ToResponseTimes() => new(P50Ms, P95Ms, RespondedSamples);
    }
}

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class DailyAggregatePreparationBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, DailyAggregateWriter aggregateWriter, ILogger<DailyAggregatePreparationBatch> logger)
{
    public async Task<RetentionBatchResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (!options.Enabled) return new(0, 0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.MaximumRunDuration);
        var token = deadline.Token;
        var stopwatch = Stopwatch.StartNew();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await RetentionTransactionLock.AcquireAsync(database, token);
        var candidates = await database.Database.SqlQueryRaw<AggregateCandidate>("""
            SELECT result.endpoint_monitor_id AS "EndpointMonitorId",
                (result.measured_at AT TIME ZONE 'UTC')::date AS "UtcDate"
            FROM web_health.check_result result
            LEFT JOIN web_health.monitoring_daily_aggregate aggregate
                ON aggregate.endpoint_monitor_id = result.endpoint_monitor_id
                AND aggregate.utc_date = (result.measured_at AT TIME ZONE 'UTC')::date
            WHERE result.measured_at < @today
                AND (aggregate.endpoint_monitor_id IS NULL OR
                    (aggregate.raw_deletion_started_at IS NULL AND aggregate.exact_duration_samples IS NULL))
            GROUP BY result.endpoint_monitor_id, (result.measured_at AT TIME ZONE 'UTC')::date
            ORDER BY (result.measured_at AT TIME ZONE 'UTC')::date, result.endpoint_monitor_id
            LIMIT @batch_size
            """, new NpgsqlParameter("today", today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            new NpgsqlParameter("batch_size", options.BatchSize)).ToArrayAsync(token);
        if (!options.DryRun && candidates.Length > 0)
        {
            foreach (var candidate in candidates)
                await aggregateWriter.RecomputeAsync(candidate.EndpointMonitorId, candidate.UtcDate, token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Daily aggregate preparation selected {SelectedCount} monitor-days in {DurationMs} ms; dry run {DryRun}",
            candidates.Length, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(candidates.Length, 0);
    }

    private sealed record AggregateCandidate(Guid EndpointMonitorId, DateOnly UtcDate);
}

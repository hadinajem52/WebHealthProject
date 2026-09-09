using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class RawResultRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, DailyAggregateWriter aggregateWriter, ILogger<RawResultRetentionBatch> logger)
{
    public async Task<RetentionBatchResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (!options.Enabled) return new(0, 0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.MaximumRunDuration);
        var token = deadline.Token;
        var stopwatch = Stopwatch.StartNew();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await RetentionTransactionLock.AcquireAsync(database, token);
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-90);
        var checks = new RetentionHistoryQueries(database, now).EligibleCompletedCheckIds(cutoff);
        var candidates = database.CheckResults.Where(result => result.MeasuredAt < cutoff && checks.Contains(result.LogicalCheckId)
            && !database.SeoObservations.Any(item => item.LogicalCheckId == result.LogicalCheckId)
            && !database.CertificateObservations.Any(item => item.LogicalCheckId == result.LogicalCheckId));
        var first = await candidates.OrderBy(result => result.MeasuredAt).ThenBy(result => result.LogicalCheckId)
            .Select(result => new
            {
                result.EndpointMonitorId,
                result.MeasuredAt
            }).FirstOrDefaultAsync(token);
        if (first is null) return new(0, 0);
        var day = DateOnly.FromDateTime(first.MeasuredAt.UtcDateTime);
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = start.AddDays(1);
        var ids = await candidates.Where(result => result.EndpointMonitorId == first.EndpointMonitorId
                && result.MeasuredAt >= start && result.MeasuredAt < end)
            .OrderBy(result => result.MeasuredAt).ThenBy(result => result.LogicalCheckId)
            .Select(result => result.LogicalCheckId).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun)
        {
            var aggregate = await database.MonitoringDailyAggregates.SingleOrDefaultAsync(
                item => item.EndpointMonitorId == first.EndpointMonitorId && item.UtcDate == day, token);
            if (aggregate is not null) await database.Entry(aggregate).ReloadAsync(token);
            if (aggregate?.RawDeletionStartedAt is null)
            {
                if (!await aggregateWriter.RecomputeAsync(first.EndpointMonitorId, day, token))
                    throw new InvalidOperationException("Raw retention requires a complete daily aggregate.");
                aggregate = await database.MonitoringDailyAggregates.SingleAsync(
                    item => item.EndpointMonitorId == first.EndpointMonitorId && item.UtcDate == day, token);
                aggregate.RawDeletionStartedAt = timeProvider.GetUtcNow();
                aggregate.ExactDurationSamples = null;
                await database.SaveChangesAsync(token);
            }
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            var eligibleIds = candidates.Where(result => ids.Contains(result.LogicalCheckId)).Select(result => result.LogicalCheckId);
            await database.Findings.Where(item => eligibleIds.Contains(item.LogicalCheckId)).ExecuteDeleteAsync(token);
            await database.RedirectHops.Where(item => eligibleIds.Contains(item.LogicalCheckId)).ExecuteDeleteAsync(token);
            deleted = await candidates.Where(result => ids.Contains(result.LogicalCheckId)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention raw-result selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}

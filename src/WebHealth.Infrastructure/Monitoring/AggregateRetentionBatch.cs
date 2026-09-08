using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class AggregateRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<AggregateRetentionBatch> logger)
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
        var cutoffDay = DateOnly.FromDateTime(now.AddMonths(-24).UtcDateTime);
        var holds = new RetentionHoldQueries(database, now);
        var heldMonitors = holds.MonitorIds();
        var heldChecks = holds.LogicalCheckIds();
        var candidates = database.MonitoringDailyAggregates.Where(item => item.UtcDate < cutoffDay
            && item.RawDeletionStartedAt != null && !heldMonitors.Contains(item.EndpointMonitorId)
            && !database.LogicalChecks.Any(check => check.EndpointMonitorId == item.EndpointMonitorId && heldChecks.Contains(check.Id))
            && !database.CheckResults.Any(result => result.EndpointMonitorId == item.EndpointMonitorId
                && DateOnly.FromDateTime(result.MeasuredAt.UtcDateTime) == item.UtcDate));
        var monitorId = await candidates.OrderBy(item => item.UtcDate).ThenBy(item => item.EndpointMonitorId)
            .Select(item => (Guid?)item.EndpointMonitorId).FirstOrDefaultAsync(token);
        if (monitorId is null) return new(0, 0);
        var selected = candidates.Where(item => item.EndpointMonitorId == monitorId);
        var dates = await selected.OrderBy(item => item.UtcDate).Select(item => item.UtcDate).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && dates.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            deleted = await selected.Where(item => dates.Contains(item.UtcDate)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention daily-aggregate selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            dates.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(dates.Length, deleted);
    }
}

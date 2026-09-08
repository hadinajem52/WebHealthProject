using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class LogicalCheckRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<LogicalCheckRetentionBatch> logger)
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
        var eligible = new RetentionHistoryQueries(database, now).EligibleCompletedCheckIds(now.AddDays(-90));
        var candidates = database.LogicalChecks.Where(check => eligible.Contains(check.Id)
            && !database.CheckResults.Any(item => item.LogicalCheckId == check.Id)
            && !database.SeoObservations.Any(item => item.LogicalCheckId == check.Id)
            && !database.CertificateObservations.Any(item => item.LogicalCheckId == check.Id)
            && !database.ExecutionAttempts.Any(item => item.LogicalCheckId == check.Id)
            && !database.DurableWork.Any(item => item.LogicalCheckId == check.Id)
            && !database.IncidentEvidence.Any(item => item.LogicalCheckId == check.Id));
        var ids = await candidates.OrderBy(check => check.CompletedAt).ThenBy(check => check.Id)
            .Select(check => check.Id).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            var selected = candidates.Where(check => ids.Contains(check.Id));
            await database.CheckConfigurationSnapshots.Where(snapshot => selected.Select(check => check.Id).Contains(snapshot.LogicalCheckId))
                .ExecuteDeleteAsync(token);
            deleted = await selected.ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention logical-check selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed record RetentionBatchResult(int Selected, int Deleted);

internal sealed class ExecutionHistoryRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<ExecutionHistoryRetentionBatch> logger)
{
    public Task<RetentionBatchResult> ExecuteAsync(CancellationToken cancellationToken = default) => ExecuteCoreAsync(false, cancellationToken);

    public Task<RetentionBatchResult> ExecuteWorkAsync(CancellationToken cancellationToken = default) => ExecuteCoreAsync(true, cancellationToken);

    private async Task<RetentionBatchResult> ExecuteCoreAsync(bool durableWork, CancellationToken cancellationToken)
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
        var attempts = database.ExecutionAttempts.Where(attempt => attempt.FinishedAt != null && attempt.FinishedAt < cutoff
            && checks.Contains(attempt.LogicalCheckId));
        var work = database.DurableWork.Where(item => item.State == "Completed" && item.UpdatedAt < cutoff
            && item.LeaseOwnerToken == null && item.LeaseAcquiredAt == null && item.LeaseExpiresAt == null
            && checks.Contains(item.LogicalCheckId));
        var candidates = durableWork
            ? work.OrderBy(item => item.UpdatedAt).ThenBy(item => item.Id).Select(item => item.Id)
            : attempts.OrderBy(item => item.FinishedAt).ThenBy(item => item.Id).Select(item => item.Id);
        var ids = await candidates.Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            deleted = durableWork
                ? await work.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(token)
                : await attempts.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention {Operation} selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            durableWork ? "durable-work" : "execution-attempt", ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}

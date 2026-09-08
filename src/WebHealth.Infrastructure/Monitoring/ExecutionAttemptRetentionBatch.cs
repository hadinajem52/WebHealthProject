using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Incidents;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed record RetentionBatchResult(int Selected, int Deleted);

internal sealed class ExecutionAttemptRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<ExecutionAttemptRetentionBatch> logger)
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
        var held = new RetentionHoldQueries(database, now).LogicalCheckIds();
        var activeIncidentStatuses = IncidentStatuses.Active.ToArray();
        var ids = await database.ExecutionAttempts.Where(attempt => attempt.FinishedAt != null && attempt.FinishedAt < cutoff
            && attempt.LogicalCheck.State == "Completed" && attempt.LogicalCheck.CompletedAt < cutoff
            && !held.Contains(attempt.LogicalCheckId)
            && !database.ExecutionLeases.Any(lease => lease.LogicalCheckId == attempt.LogicalCheckId)
            && !database.EndpointHealth.Any(health => health.EvidenceLogicalCheckId == attempt.LogicalCheckId)
            && !database.IncidentEvidence.Any(evidence => evidence.LogicalCheckId == attempt.LogicalCheckId
                && activeIncidentStatuses.Contains(evidence.Incident.Status)))
            .OrderBy(attempt => attempt.FinishedAt).ThenBy(attempt => attempt.Id)
            .Select(attempt => attempt.Id).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            deleted = await database.ExecutionAttempts.Where(attempt => ids.Contains(attempt.Id)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention {Operation} selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            "execution-attempt", ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}

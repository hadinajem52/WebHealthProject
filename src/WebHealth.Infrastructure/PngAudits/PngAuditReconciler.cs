using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngAuditReconciler(
    ApplicationDbContext database,
    PngAuditOptions options,
    TimeProvider timeProvider,
    ILogger<PngAuditReconciler> logger) : IPngAuditReconciler
{
    public async Task<IReadOnlyList<Guid>> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var queuedBefore = now.Subtract(options.ReconciliationDelay);
        var candidates = await database.PngAuditRuns.AsNoTracking()
            .Where(run =>
                (run.Status == PngAuditRunStatuses.Queued && run.UpdatedAt < queuedBefore)
                || (run.Status == PngAuditRunStatuses.Running
                    && run.LeaseExpiresAt != null
                    && run.LeaseExpiresAt < now))
            .OrderBy(run => run.UpdatedAt)
            .ThenBy(run => run.Id)
            .Take(options.ReconciliationBatchSize)
            .Select(run => new { run.Id, run.AttemptCount })
            .ToArrayAsync(cancellationToken);

        var exhausted = candidates
            .Where(run => run.AttemptCount >= options.MaximumAttempts)
            .Select(run => run.Id)
            .ToArray();
        if (exhausted.Length > 0)
        {
            await database.PngAuditRuns
                .Where(run => exhausted.Contains(run.Id)
                    && (run.Status == PngAuditRunStatuses.Queued
                        || run.Status == PngAuditRunStatuses.Running))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, PngAuditRunStatuses.Failed)
                    .SetProperty(run => run.FailureCode, PngAuditFailureCodes.AttemptsExhausted)
                    .SetProperty(run => run.SafeDiagnostic,
                        "The PNG audit did not finish within its allowed execution attempts.")
                    .SetProperty(run => run.LeaseToken, (Guid?)null)
                    .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(run => run.FinishedAt, now)
                    .SetProperty(run => run.UpdatedAt, now),
                    cancellationToken);
        }

        var recoverable = candidates
            .Where(run => run.AttemptCount < options.MaximumAttempts)
            .Select(run => run.Id)
            .ToArray();
        if (candidates.Length > 0)
        {
            database.ChangeTracker.Clear();
            logger.LogInformation(
                "Reconciled PNG audit runs. RecoverableCount={RecoverableCount} "
                + "ExhaustedCount={ExhaustedCount}",
                recoverable.Length,
                exhausted.Length);
        }

        return recoverable;
    }
}

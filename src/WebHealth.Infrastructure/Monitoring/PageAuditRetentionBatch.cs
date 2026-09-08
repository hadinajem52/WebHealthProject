using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class PageAuditRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<PageAuditRetentionBatch> logger)
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
        var held = new RetentionHoldQueries(database, now).PageAuditRunIds();
        var terminal = database.PageAuditRuns.Where(run => run.FinishedAt != null
            && run.Status != "Queued" && run.Status != "Running");
        var scored = PageAuditHistoryQueries.Scored(database.PageAuditRuns);
        var candidates = terminal.Where(run => run.FinishedAt < cutoff && !held.Contains(run.Id)
            && run.LeaseToken == null && run.LeaseExpiresAt == null
            && !database.IncidentEvidence.Any(evidence => evidence.PageAuditRunId == run.Id)
            && terminal.Any(newer => newer.PageAuditTargetId == run.PageAuditTargetId && newer.Strategy == run.Strategy
                && (newer.FinishedAt > run.FinishedAt || (newer.FinishedAt == run.FinishedAt && newer.Id > run.Id)))
            && (!scored.Any(item => item.Id == run.Id)
                || scored.Count(newer => newer.PageAuditTargetId == run.PageAuditTargetId && newer.Strategy == run.Strategy
                    && newer.Locale == run.Locale
                    && (newer.FinishedAt > run.FinishedAt || (newer.FinishedAt == run.FinishedAt && newer.Id > run.Id))) >= 2));
        var ids = await candidates.OrderBy(run => run.FinishedAt).ThenBy(run => run.Id)
            .Select(run => run.Id).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            var selected = candidates.Where(run => ids.Contains(run.Id));
            await database.PageAuditItems.Where(item => selected.Select(run => run.Id).Contains(item.RunId)).ExecuteDeleteAsync(token);
            deleted = await selected.ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention page-audit-run selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}

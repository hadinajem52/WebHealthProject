using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class CrawlRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<CrawlRetentionBatch> logger)
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
        var held = new RetentionHoldQueries(database, now).CrawlRunIds();
        var terminal = database.CrawlRuns.Where(run => run.Status != "Running" && run.FinishedAt != null);
        var comparable = CrawlHistoryQueries.Comparable(database.CrawlRuns);
        var candidates = terminal.Where(run => run.FinishedAt < cutoff && !held.Contains(run.Id)
            && terminal.Any(newer => newer.EndpointId == run.EndpointId
                && (newer.StartedAt > run.StartedAt || (newer.StartedAt == run.StartedAt && newer.Id > run.Id)))
            && (!comparable.Any(item => item.Id == run.Id)
                || comparable.Count(newer => newer.EndpointId == run.EndpointId
                    && (newer.StartedAt > run.StartedAt || (newer.StartedAt == run.StartedAt && newer.Id > run.Id))) >= 2));
        var ids = await candidates.OrderBy(run => run.FinishedAt).ThenBy(run => run.Id)
            .Select(run => run.Id).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            var selected = candidates.Where(run => ids.Contains(run.Id));
            await database.CrawlLinkResults.Where(link => selected.Select(run => run.Id).Contains(link.RunId)).ExecuteDeleteAsync(token);
            deleted = await selected.ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention crawl-run selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}

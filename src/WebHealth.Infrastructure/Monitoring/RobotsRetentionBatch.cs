using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Seo;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class RobotsRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<RobotsRetentionBatch> logger)
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
        var heldEndpoints = new RetentionHoldQueries(database, now).RelatedEndpointIds();
        var candidates = database.RobotsSnapshots.Where(snapshot => snapshot.FetchedAt < cutoff && snapshot.UpdatedAt < cutoff
            && snapshot.ExpiresAt <= now && snapshot.ExceptionReason == null
            && !snapshot.SitemapRequired && snapshot.ConfiguredSitemapUrl == null
            && !database.Endpoints.Any(endpoint => heldEndpoints.Contains(endpoint.Id)
                && (endpoint.NormalizedUrl == snapshot.Origin || endpoint.NormalizedUrl.StartsWith(snapshot.Origin + "/"))));
        var origins = await candidates.OrderBy(snapshot => snapshot.UpdatedAt).ThenBy(snapshot => snapshot.Origin)
            .Select(snapshot => snapshot.Origin).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && origins.Length > 0)
        {
            foreach (var origin in origins.Order(StringComparer.Ordinal))
                await RobotsOriginLock.AcquireAsync(database, origin, token);
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            deleted = await candidates.Where(snapshot => origins.Contains(snapshot.Origin)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention robots-snapshot selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            origins.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(origins.Length, deleted);
    }
}

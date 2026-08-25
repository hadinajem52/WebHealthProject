using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

public sealed class CrawlRunJob(
    ICrawlExecutionService executionService,
    CrawlQueuedRunReader requestReader)
{
    [Queue(CrawlQueueNames.Crawl)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var request = await requestReader.ReadAsync(runId, cancellationToken);
        if (request is not null)
        {
            await executionService.ExecuteAsync(request, cancellationToken);
        }
    }
}

public sealed class CrawlQueuedRunReader(ApplicationDbContext dbContext)
{
    public async Task<CrawlRunRequest?> ReadAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.CrawlRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new
            {
                run.Id,
                run.EndpointId,
                run.Endpoint.NormalizedUrl,
                run.Endpoint.Environment.IsProduction,
                run.AllowedHosts,
                run.AllowedPathPrefixes,
                run.QueryPolicy,
                run.MaxPages,
                run.MaxDepth,
                run.CheckExternalLinks
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        Enum.TryParse<CrawlQueryPolicy>(stored.QueryPolicy, out var queryPolicy);
        return new(stored.Id, stored.EndpointId, stored.IsProduction, [stored.NormalizedUrl])
        {
            AllowedHosts = stored.AllowedHosts is null
                ? null
                : Split(stored.AllowedHosts)
                    .Select(value => value.StartsWith("*.", StringComparison.Ordinal)
                        ? new CrawlHostRule(value[2..], true)
                        : new CrawlHostRule(value, false))
                    .ToArray(),
            AllowedPathPrefixes = stored.AllowedPathPrefixes is null
                ? null
                : Split(stored.AllowedPathPrefixes),
            Limits = new() { MaxPages = stored.MaxPages, MaxDepth = stored.MaxDepth },
            UrlOptions = new() { QueryPolicy = queryPolicy },
            CheckExternalLinks = stored.CheckExternalLinks,
            RequestRobotsOverride = true
        };
    }

    private static string[] Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class CrawlReconciliationJob(ICrawlReconciler reconciler)
{
    [Queue(CrawlQueueNames.Crawl)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ReconcileAsync(CancellationToken cancellationToken) =>
        await reconciler.RetireAbandonedRunsAsync(cancellationToken);
}

public static class CrawlSchedulingApplicationBuilderExtensions
{
    public static WebApplication UseCrawlScheduling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<CrawlSchedulingOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<CrawlReconciliationJob>(
            "crawl-reconciliation",
            CrawlQueueNames.Crawl,
            job => job.ReconcileAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions());
        return app;
    }
}

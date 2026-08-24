using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// The crawl queue's only job. It is deliberately the sole occupant of its own Hangfire server, so
/// a crawl can never take a worker a scheduled check is waiting for — see
/// docs/phase-6/Crawl_Execution_And_Isolation.md.
/// <para>
/// No automatic retry. A run that failed part-way has already written what it found; re-running it
/// from the start would repeat every request against a target we do not own, which is precisely
/// what the limits in this phase exist to prevent. A new run is an explicit decision.
/// </para>
/// </summary>
public sealed class CrawlRunJob(ICrawlExecutionService executionService)
{
    [Queue(CrawlQueueNames.Crawl)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(
        Guid runId,
        Guid endpointId,
        bool isProduction,
        string[] seedUrls,
        bool checkExternalLinks,
        bool requestRobotsOverride,
        CancellationToken cancellationToken)
    {
        await executionService.ExecuteAsync(
            new(runId, endpointId, isProduction, seedUrls ?? [])
            {
                Limits = CrawlLimits.Default,
                CheckExternalLinks = checkExternalLinks,
                RequestRobotsOverride = requestRobotsOverride
            },
            cancellationToken);
    }
}

/// <summary>
/// The reconciliation sweep, on the same isolated queue as the crawls it repairs.
/// </summary>
/// <remarks>
/// <para>
/// A crawl records its outcome from inside <see cref="CrawlRunJob" />, so a process that dies
/// mid-run leaves the row Running for ever: the page goes on reporting a crawl in progress, and
/// the endpoint's active-run index refuses every later request. Nothing else closes that row —
/// crawls have no dispatcher, and a manual request is their only trigger.
/// </para>
/// <para>
/// It shares the crawl queue rather than the shared one because that queue exists exactly when
/// crawls can, so the sweep needs no configuration of its own. A sweep therefore waits behind the
/// crawls already occupying that queue's workers, which is the accepted bound of this recovery:
/// it repairs runs whose process is gone, and after a restart no crawl is holding a worker. A
/// crawl that is hung but still alive is not repaired until its process ends -- and could not
/// safely be, since retiring a row does not stop the worker writing to it.
/// </para>
/// </remarks>
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

        // Every fifteen minutes. A run is only retired once it is past MaxDuration by a clear
        // margin, so the cadence decides how long a page keeps reporting a crawl that has already
        // gone, not how eagerly a live crawl is cut short.
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<CrawlReconciliationJob>(
            "crawl-reconciliation",
            CrawlQueueNames.Crawl,
            job => job.ReconcileAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions());
        return app;
    }
}

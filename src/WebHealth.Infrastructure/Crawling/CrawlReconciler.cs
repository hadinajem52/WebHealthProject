using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// Closes crawl runs that have been in flight far longer than a crawl can legitimately take.
/// <para>
/// A crawl records its outcome from inside its own job, so a process that dies mid-run leaves the
/// row Running for ever: the page goes on reporting a crawl in progress, and the endpoint's
/// active-run index refuses every later request. Failed rather than deleted -- the run happened,
/// and whatever links it recorded before dying stay attached to it.
/// </para>
/// </summary>
internal sealed class CrawlReconciler(
    ApplicationDbContext dbContext,
    CrawlSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<CrawlReconciler> logger) : ICrawlReconciler
{
    /// <summary>
    /// How long a run may sit in Running before it is treated as abandoned. A crawl stops itself
    /// at MaxDuration, so anything still Running well past that belongs to a process that died --
    /// and without this, one crash would block its endpoint from ever being crawled again, because
    /// ux_crawl_run_active would refuse every later request.
    /// </summary>
    private TimeSpan StaleAfter => schedulingOptions.MaxDuration + TimeSpan.FromMinutes(15);

    public Task<int> RetireAbandonedRunsAsync(CancellationToken cancellationToken = default) =>
        RetireAsync(null, cancellationToken);

    public Task<int> RetireAbandonedRunsAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        RetireAsync(endpointId, cancellationToken);

    private async Task<int> RetireAsync(Guid? endpointId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now - StaleAfter;
        var abandoned = dbContext.CrawlRuns
            .Where(run => run.Status == CrawlRunStatuses.Running && run.StartedAt < cutoff);
        if (endpointId is { } scoped)
        {
            abandoned = abandoned.Where(run => run.EndpointId == scoped);
        }

        var retired = await abandoned
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, CrawlRunStatuses.Failed)
                .SetProperty(run => run.StopReason, CrawlStopReasons.Failed)
                .SetProperty(run => run.FailureReason,
                    "Abandoned: the crawl was still running long after its time limit, so the "
                    + "process performing it is gone.")
                .SetProperty(run => run.FinishedAt, now),
                cancellationToken);

        if (retired > 0)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(
                "Retired {Retired} abandoned crawl run(s). Scope={Scope}",
                retired,
                endpointId is { } logged ? logged.ToString() : "every endpoint");
        }

        return retired;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlReconciler(
    ApplicationDbContext dbContext,
    CrawlSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<CrawlReconciler> logger) : ICrawlReconciler
{
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
                .SetProperty(run => run.FailureReason, CrawlFailureCodes.Abandoned)
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

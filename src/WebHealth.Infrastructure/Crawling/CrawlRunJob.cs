using Hangfire;
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

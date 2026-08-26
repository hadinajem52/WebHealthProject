using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

public sealed class CrawlRunner(
    ApplicationDbContext dbContext,
    ICrawlResultSink sink,
    ICrawlReconciler reconciler,
    ITargetAuthorizationService targetAuthorization,
    CrawlSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<CrawlRunner> logger,
    ICrawlRunQueue? queue = null) : ICrawlRunner
{
    public bool CanQueue => schedulingOptions.Enabled && queue is not null;

    public async Task<CrawlManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        bool checkExternalLinks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        var now = timeProvider.GetUtcNow();

        if (!CanQueue)
        {
            return CrawlManualResult.Rejected(
                "Crawls are not running on this instance, so a crawl cannot be started. "
                + "Enable Crawling:Scheduling to run them.");
        }

        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return CrawlManualResult.NotTestable(
                await targetAuthorization.DescribeTestBlockAsync(endpointId, access, cancellationToken));
        }

        var endpoint = await MonitoringEligibility
            .ApplyTestable(dbContext.Endpoints.AsNoTracking())
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new
            {
                candidate.NormalizedUrl,
                candidate.Environment.IsProduction
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return CrawlManualResult.Rejected(
                "The endpoint is not active.");
        }

        await reconciler.RetireAbandonedRunsAsync(endpointId, cancellationToken);

        var active = await FindActiveRunAsync(endpointId, cancellationToken);
        if (active is { } running)
        {
            return CrawlManualResult.AlreadyRunning(running);
        }

        var runId = Guid.NewGuid();
        var request = new CrawlRunRequest(runId, endpointId, endpoint.IsProduction, [endpoint.NormalizedUrl])
        {
            Limits = CrawlLimits.Default,
            CheckExternalLinks = checkExternalLinks,

            RequestRobotsOverride = true
        };

        try
        {
            await sink.BeginRunAsync(
                new(runId, endpointId, request.SeedUrls, CrawlRunSettings.From(request), now),
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindActiveRunAsync(endpointId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return CrawlManualResult.AlreadyRunning(winner.Value);
        }

        dbContext.ChangeTracker.Clear();
        try
        {
            queue!.Enqueue(runId);
        }
        catch (Exception exception)
        {
            await RetireUnreachableRunAsync(runId, cancellationToken);
            logger.LogError(
                exception,
                "Crawl run could not be queued and was retired. CrawlRunId={CrawlRunId} EndpointId={EndpointId}",
                runId, endpointId);
            return CrawlManualResult.Rejected(
                "The crawl could not be handed to a worker and was not started.");
        }

        logger.LogInformation(
            "Crawl run queued by request. CrawlRunId={CrawlRunId} EndpointId={EndpointId}",
            runId, endpointId);
        return CrawlManualResult.Queued(runId);
    }

    private async Task<Guid?> FindActiveRunAsync(Guid endpointId, CancellationToken cancellationToken)
    {
        var runId = await dbContext.CrawlRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId && run.Status == CrawlRunStatuses.Running)
            .Select(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return runId == Guid.Empty ? null : runId;
    }

    private async Task RetireUnreachableRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.CrawlRuns
            .Where(run => run.Id == runId && run.Status == CrawlRunStatuses.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, CrawlRunStatuses.Failed)
                .SetProperty(run => run.StopReason, CrawlStopReasons.Failed)
                .SetProperty(run => run.FailureReason, CrawlFailureCodes.WorkerUnavailable)
                .SetProperty(run => run.FinishedAt, now),
                cancellationToken);
        dbContext.ChangeTracker.Clear();
    }
}

internal sealed class HangfireCrawlRunQueue(IBackgroundJobClient backgroundJobs)
    : ICrawlRunQueue
{
    public void Enqueue(Guid runId) =>
        backgroundJobs.Enqueue<CrawlRunJob>(job => job.ExecuteAsync(
            runId,
            CancellationToken.None));
}

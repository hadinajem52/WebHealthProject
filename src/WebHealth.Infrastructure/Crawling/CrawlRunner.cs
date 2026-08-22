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

/// <summary>
/// Opens a crawl somebody asked for by hand, and is the only path in the application that starts
/// one at all.
/// </summary>
/// <remarks>
/// <para>
/// The run row is committed before the job is enqueued, never the other way round. A job that
/// arrived before its row existed would find nothing to do and vanish; a row with no job is
/// visible. The failure that survives is the one that leaves evidence — and because there is no
/// reconciliation sweep for crawls, a run that could not be enqueued is retired here rather than
/// left to hold its endpoint's active slot forever.
/// </para>
/// <para>
/// Scope is derived from the seed rather than configured: the seed is the endpoint's own
/// normalized URL, and <c>CrawlScope.FromSeeds</c> turns that into a same-host scope. Nothing here
/// invents a wider reach than the endpoint this application was already authorized to test.
/// External link checking and robots override both stay off — each is a separate permission with
/// its own evidence requirement, and neither is something a button should be able to grant.
/// </para>
/// </remarks>
public sealed class CrawlRunner(
    ApplicationDbContext dbContext,
    ICrawlResultSink sink,
    ITargetAuthorizationService targetAuthorization,
    CrawlSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<CrawlRunner> logger,
    ICrawlRunQueue? queue = null) : ICrawlRunner
{
    /// <summary>
    /// How long a run may sit in Running before it is treated as abandoned. A crawl stops itself
    /// at MaxDuration, so anything still Running well past that belongs to a process that died --
    /// and without this, one crash would block its endpoint from ever being crawled again, because
    /// ux_crawl_run_active would refuse every later request.
    /// </summary>
    private TimeSpan StaleAfter => schedulingOptions.MaxDuration + TimeSpan.FromMinutes(15);

    public bool CanQueue => schedulingOptions.Enabled && queue is not null;

    public async Task<CrawlManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        var now = timeProvider.GetUtcNow();

        // Checked before anything is written. Opening a run first and discovering afterwards that
        // no worker exists leaves a row in Running holding this endpoint's only active-crawl slot,
        // which is a worse outcome than refusing. Note this is not the same as the enqueue failing:
        // with the crawl queue unserved but Hangfire otherwise up, Enqueue succeeds and the job is
        // simply never picked up, so no exception would ever be raised to catch.
        if (!CanQueue)
        {
            return CrawlManualResult.Rejected(
                "Crawls are not running on this instance, so a crawl cannot be started. "
                + "Enable Crawling:Scheduling to run them.");
        }

        // Enforced here rather than trusted from the caller. Fetching a site's pages is active
        // testing of that target, and this method is the only door to it.
        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return CrawlManualResult.Rejected(
                "You are not authorized to run a crawl against this endpoint.");
        }

        var endpoint = await MonitoringEligibility
            .ApplyTestable(dbContext.Endpoints.AsNoTracking(), now)
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
                "The endpoint is not active, or its target authorization has lapsed.");
        }

        await RetireAbandonedRunsAsync(endpointId, now, cancellationToken);

        var active = await FindActiveRunAsync(endpointId, cancellationToken);
        if (active is { } running)
        {
            return CrawlManualResult.AlreadyRunning(running);
        }

        var runId = Guid.NewGuid();
        var request = new CrawlRunRequest(runId, endpointId, endpoint.IsProduction, [endpoint.NormalizedUrl])
        {
            Limits = CrawlLimits.Default,
            CheckExternalLinks = false,
            RequestRobotsOverride = false
        };

        try
        {
            // The sink owns how a run row is shaped, so opening one here cannot drift from the
            // row the execution service would have opened. It finds this row already present and
            // continues against it.
            await sink.BeginRunAsync(
                new(runId, endpointId, request.SeedUrls, CrawlRunSettings.From(request), now),
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            // ux_crawl_run_active caught a request that raced another. The other run is doing
            // exactly what this one would have, so it is the answer rather than an error.
            // Anything else is a real failure and is rethrown.
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
            queue!.Enqueue(
                runId,
                endpointId,
                endpoint.IsProduction,
                request.SeedUrls,
                request.CheckExternalLinks,
                request.RequestRobotsOverride);
        }
        catch (Exception exception)
        {
            // With crawl scheduling switched off there is no server serving the crawl queue, so a
            // committed run would sit Running forever and its endpoint's active-run index would
            // refuse every later request. Retiring it keeps the failure to the one request that
            // caused it.
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

    /// <summary>
    /// Closes runs that have been in flight far longer than a crawl can legitimately take. There
    /// is no reconciliation sweep for crawls, so without this a process that died mid-run would
    /// block its endpoint permanently. Failed rather than deleted: the run happened, and whatever
    /// links it recorded before dying stay attached to it.
    /// </summary>
    private async Task RetireAbandonedRunsAsync(
        Guid endpointId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cutoff = now - StaleAfter;
        var retired = await dbContext.CrawlRuns
            .Where(run => run.EndpointId == endpointId
                && run.Status == CrawlRunStatuses.Running
                && run.StartedAt < cutoff)
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
                "Retired {Retired} abandoned crawl run(s). EndpointId={EndpointId}",
                retired, endpointId);
        }
    }

    private async Task<Guid?> FindActiveRunAsync(Guid endpointId, CancellationToken cancellationToken)
    {
        var runId = await dbContext.CrawlRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId && run.Status == CrawlRunStatuses.Running)
            .Select(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return runId == Guid.Empty ? null : runId;
    }

    /// <summary>
    /// Closes a run whose job never reached a worker. Failed rather than deleted: the run was
    /// opened, and a reader looking for what happened deserves the reason rather than a gap.
    /// </summary>
    private async Task RetireUnreachableRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.CrawlRuns
            .Where(run => run.Id == runId && run.Status == CrawlRunStatuses.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, CrawlRunStatuses.Failed)
                .SetProperty(run => run.StopReason, CrawlStopReasons.Failed)
                .SetProperty(run => run.FailureReason,
                    "No crawl worker is running on this instance, so the crawl was never started.")
                .SetProperty(run => run.FinishedAt, now),
                cancellationToken);
        dbContext.ChangeTracker.Clear();
    }
}

/// <summary>
/// The Hangfire side of <see cref="ICrawlRunQueue" />. The job carries the request as arguments
/// rather than re-reading it, so a crawl cannot be steered by editing the endpoint after it was
/// asked for.
/// </summary>
internal sealed class HangfireCrawlRunQueue(IBackgroundJobClient backgroundJobs)
    : ICrawlRunQueue
{
    public void Enqueue(
        Guid runId,
        Guid endpointId,
        bool isProduction,
        IReadOnlyList<string> seedUrls,
        bool checkExternalLinks,
        bool requestRobotsOverride) =>
        backgroundJobs.Enqueue<CrawlRunJob>(job => job.ExecuteAsync(
            runId,
            endpointId,
            isProduction,
            seedUrls.ToArray(),
            checkExternalLinks,
            requestRobotsOverride,
            CancellationToken.None));
}

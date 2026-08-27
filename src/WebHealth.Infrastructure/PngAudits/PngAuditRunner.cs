using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PngAudits;

public sealed class PngAuditRunner(
    ApplicationDbContext database,
    IPngAuditResultSink sink,
    IEndpointTestGate testGate,
    PngAuditOptions options,
    TimeProvider timeProvider,
    ILogger<PngAuditRunner> logger,
    IPngAuditRunQueue queue) : IPngAuditRunner
{
    public bool CanQueue => options.Enabled;

    public async Task<PngAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!CanQueue)
        {
            return PngAuditManualResult.Rejected(
                "PNG audits are not running on this instance. Enable PngAudits to run them.");
        }

        if (!await testGate.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return PngAuditManualResult.NotTestable(
                await testGate.DescribeTestBlockAsync(endpointId, access, cancellationToken));
        }

        var endpoint = await MonitoringEligibility
            .ApplyTestable(database.Endpoints.AsNoTracking())
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new
            {
                candidate.NormalizedUrl,
                candidate.Environment.IsProduction
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return PngAuditManualResult.Rejected("The endpoint is not active.");
        }

        var activeRun = await FindActiveRunAsync(endpointId, cancellationToken);
        if (activeRun is { } existing)
        {
            return PngAuditManualResult.AlreadyRunning(existing);
        }

        var snapshot = CreateSnapshot(endpointId, endpoint.NormalizedUrl, endpoint.IsProduction);
        var runId = Guid.CreateVersion7();
        try
        {
            await sink.CreateQueuedRunAsync(
                new(
                    runId,
                    PngAuditSources.Manual,
                    access.UserId,
                    snapshot,
                    timeProvider.GetUtcNow()),
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindActiveRunAsync(endpointId, cancellationToken);
            if (winner is null) throw;
            return PngAuditManualResult.AlreadyRunning(winner.Value);
        }

        database.ChangeTracker.Clear();
        try
        {
            queue.Enqueue(runId);
        }
        catch (Exception exception)
        {
            await sink.FailAsync(
                runId,
                null,
                PngAuditRunStatuses.Failed,
                PngAuditFailureCodes.WorkerUnavailable,
                "The PNG audit could not be handed to a worker.",
                CancellationToken.None);
            logger.LogError(
                exception,
                "PNG audit run could not be queued and was retired. PngAuditRunId={PngAuditRunId} EndpointId={EndpointId}",
                runId,
                endpointId);
            return PngAuditManualResult.Rejected(
                "The PNG audit could not be handed to a worker and was not started.");
        }

        logger.LogInformation(
            "PNG audit run queued by request. PngAuditRunId={PngAuditRunId} EndpointId={EndpointId}",
            runId,
            endpointId);
        return PngAuditManualResult.Queued(runId);
    }

    private async Task<Guid?> FindActiveRunAsync(
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var runId = await database.PngAuditRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId
                && (run.Status == PngAuditRunStatuses.Queued
                    || run.Status == PngAuditRunStatuses.Running))
            .Select(run => run.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return runId == Guid.Empty ? null : runId;
    }

    private PngAuditRunSnapshot CreateSnapshot(
        Guid endpointId,
        string seedUrl,
        bool isProduction)
    {
        var urlOptions = CrawlUrlOptions.Default;
        var normalized = CrawlUrlNormalizer.Normalize(seedUrl, urlOptions).Url
            ?? throw new InvalidOperationException("The endpoint URL is not a valid crawl seed.");
        var hostRule = new CrawlHostRule(normalized.Host);
        return new(
            endpointId,
            normalized.Value,
            isProduction,
            [hostRule],
            [normalized.Directory],
            [hostRule],
            urlOptions,
            new PngSiteDiscoveryProfile(
                new PngPageDiscoveryLimits(
                    options.MaxPages,
                    options.MaxDepth,
                    options.MaxPageBytes,
                    options.MaxTotalPageBytes,
                    options.MaxImageReferencesPerPage),
                new PngImageDiscoveryLimits(
                    options.MaxUniqueImages,
                    options.MaxTotalImageSourceMappings),
                new PngDiscoveryFetchPolicy(
                    options.MaxTotalHttpAttempts,
                    options.FetchTimeoutSeconds,
                    options.RequestsPerSecondPerHost,
                    options.TransientRetryCount,
                    options.MaxDuration)),
            new PngImageAnalysisLimits(
                options.MaxImageBytes,
                options.MaxWidth,
                options.MaxHeight,
                options.MaxDecodedPixels,
                options.MaxDecodedMemoryBytes),
            options.MaxTotalImageBytes,
            new PngRecommendationThresholds(
                options.MinSavingsPercent,
                options.MinSavingsBytes),
            PngAnalysisProfiles.Analyzer,
            PngAnalysisProfiles.Comparison);
    }
}

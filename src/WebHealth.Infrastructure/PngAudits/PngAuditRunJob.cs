using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PngAudits;

public static class PngAuditQueueNames
{
    public const string ImageAudits = "image-audits";
}

public sealed class PngAuditRunJob(PngAuditExecutionService executionService)
{
    [Queue(PngAuditQueueNames.ImageAudits)]
    [AutomaticRetry(Attempts = 0)]
    public Task ExecuteAsync(Guid runId, CancellationToken cancellationToken) =>
        executionService.ExecuteAsync(runId, cancellationToken);
}

internal sealed class HangfirePngAuditRunQueue(IBackgroundJobClient backgroundJobs)
    : IPngAuditRunQueue
{
    public void Enqueue(Guid runId) =>
        backgroundJobs.Enqueue<PngAuditRunJob>(job => job.ExecuteAsync(
            runId,
            CancellationToken.None));
}

internal sealed class DisabledPngAuditRunQueue : IPngAuditRunQueue
{
    public void Enqueue(Guid runId) => throw new InvalidOperationException(
        "PNG audit scheduling is disabled; no queue is available to run PNG audits.");
}

internal enum PngAuditTargetState
{
    Ready,
    Ineligible,
    Changed
}

internal sealed record PngAuditExecutionTarget(
    PngAuditTargetState State,
    string? SeedUrl)
{
    public static PngAuditExecutionTarget Ready(string seedUrl) =>
        new(PngAuditTargetState.Ready, seedUrl);

    public static PngAuditExecutionTarget Ineligible() =>
        new(PngAuditTargetState.Ineligible, null);

    public static PngAuditExecutionTarget Changed() =>
        new(PngAuditTargetState.Changed, null);
}

internal sealed record PngAuditStoredProgress(
    IReadOnlySet<string> ImageIdentityHashes,
    int PagesDiscovered,
    int HttpAttempts,
    long TotalPageBytes,
    int ImageCount,
    int AnalyzedCount,
    int RecommendationCount,
    int DiscoverySkipCount,
    int SourceMappingCount,
    long TotalImageBytes,
    bool HasImageProblems,
    bool CrawlCoverageLimited,
    bool ImageAnalysisCoverageLimited,
    bool SourceMappingCoverageLimited);

public sealed class PngAuditQueuedRunReader(ApplicationDbContext database)
{
    internal async Task<PngAuditExecutionTarget> ReadAsync(
        PngAuditRunClaim claim,
        CancellationToken cancellationToken)
    {
        var endpoint = await MonitoringEligibility
            .ApplyTestable(database.Endpoints.AsNoTracking())
            .Where(candidate => candidate.Id == claim.Snapshot.EndpointId)
            .Select(candidate => new
            {
                candidate.NormalizedUrl,
                candidate.Environment.IsProduction
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return PngAuditExecutionTarget.Ineligible();
        }

        var normalized = CrawlUrlNormalizer.Normalize(
            endpoint.NormalizedUrl,
            claim.Snapshot.UrlOptions).Url;
        if (normalized is null || endpoint.IsProduction != claim.Snapshot.IsProduction)
        {
            return PngAuditExecutionTarget.Changed();
        }

        var expectedHash = Convert.FromHexString(claim.SeedIdentityHash);
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized.Value));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash)
            ? PngAuditExecutionTarget.Ready(normalized.Value)
            : PngAuditExecutionTarget.Changed();
    }


    internal async Task<PngAuditStoredProgress> ReadProgressAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await database.PngAuditRuns.AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new
            {
                candidate.PagesDiscovered,
                candidate.HttpAttempts,
                candidate.TotalPageBytes
            })
            .SingleAsync(cancellationToken);
        var images = database.PngAuditImageResults.AsNoTracking()
            .Where(result => result.RunId == runId);
        var storedImages = await images
            .Select(result => new
            {
                Hash = result.ImageIdentityHash,
                result.Classification,
                result.Recommendation,
                result.ResponseBytes
            })
            .ToArrayAsync(cancellationToken);
        var discoverySkipCount = await database.PngAuditDiscoverySkips.AsNoTracking()
            .CountAsync(skip => skip.RunId == runId, cancellationToken);
        var imageIds = images.Select(result => result.Id);
        var sourceMappingCount = await database.PngAuditImageSources.AsNoTracking()
            .CountAsync(source => imageIds.Contains(source.ImageResultId), cancellationToken);
        var coverageAreas = await database.PngAuditCoverageReasons.AsNoTracking()
            .Where(reason => reason.RunId == runId)
            .Select(reason => reason.Area)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return new(
            storedImages
                .Select(image => Convert.ToHexString(image.Hash).ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal),
            run.PagesDiscovered,
            run.HttpAttempts,
            run.TotalPageBytes,
            storedImages.Length,
            storedImages.Count(image =>
                image.Classification != PngAuditImageClassifications.FetchFailed
                && image.Classification != PngAuditImageClassifications.HttpNonSuccess
                && image.Classification != PngAuditImageClassifications.ResponseTruncated),
            storedImages.Count(image =>
                image.Recommendation == PngAuditRecommendations.LosslessWebp),
            discoverySkipCount,
            sourceMappingCount,
            storedImages.Sum(image => image.ResponseBytes),
            storedImages.Any(image => image.Classification is
                PngAuditImageClassifications.FetchFailed
                or PngAuditImageClassifications.HttpNonSuccess
                or PngAuditImageClassifications.ResponseTruncated
                or PngAuditImageClassifications.IdentificationFailed
                or PngAuditImageClassifications.UnsupportedBitDepth
                or PngAuditImageClassifications.DimensionsExceeded
                or PngAuditImageClassifications.PixelLimitExceeded
                or PngAuditImageClassifications.DecodedMemoryExceeded
                or PngAuditImageClassifications.DecodeFailed
                or PngAuditImageClassifications.WebpComparisonFailed),
            coverageAreas.Contains(PngCoverageArea.Crawl.ToString(), StringComparer.Ordinal),
            coverageAreas.Contains(PngCoverageArea.ImageAnalysis.ToString(), StringComparer.Ordinal),
            coverageAreas.Contains(PngCoverageArea.SourceMappings.ToString(), StringComparer.Ordinal));
    }
}

public sealed class PngAuditReconciliationJob(
    IPngAuditReconciler reconciler,
    IPngAuditRunQueue queue,
    ILogger<PngAuditReconciliationJob> logger)
{
    [Queue(PngAuditQueueNames.ImageAudits)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var runIds = await reconciler.ReconcileAsync(cancellationToken);
        foreach (var runId in runIds)
        {
            try
            {
                queue.Enqueue(runId);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Recoverable PNG audit run could not be re-enqueued. PngAuditRunId={PngAuditRunId}",
                    runId);
            }
        }
    }
}

public static class PngAuditSchedulingApplicationBuilderExtensions
{
    public static WebApplication UsePngAuditScheduling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<PngAuditOptions>();
        if (!options.Enabled) return app;

        app.Services.GetRequiredService<IRecurringJobManager>()
            .AddOrUpdate<PngAuditReconciliationJob>(
                "png-audit-reconciliation",
                PngAuditQueueNames.ImageAudits,
                job => job.ReconcileAsync(CancellationToken.None),
                Cron.Minutely(),
                new RecurringJobOptions());
        return app;
    }
}

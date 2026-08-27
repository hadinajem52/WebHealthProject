using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.PngAudits;

namespace WebHealth.Application.PngAudits;

public sealed record PngAuditRunSnapshot
{
    public PngAuditRunSnapshot(
        Guid endpointId,
        string seedUrl,
        bool isProduction,
        IReadOnlyList<CrawlHostRule> allowedPageHosts,
        IReadOnlyList<string> allowedPagePathPrefixes,
        IReadOnlyList<CrawlHostRule> allowedAssetHosts,
        CrawlUrlOptions urlOptions,
        PngSiteDiscoveryProfile discoveryProfile,
        PngImageAnalysisLimits analysisLimits,
        long maxTotalImageBytes,
        PngRecommendationThresholds recommendationThresholds,
        string analyzerProfile,
        string comparisonProfile)
    {
        if (endpointId == Guid.Empty)
        {
            throw new ArgumentException("A PNG audit snapshot needs an endpoint.", nameof(endpointId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(seedUrl);
        if (seedUrl.Length > CrawlUrlOptions.MaxUrlLength)
        {
            throw new ArgumentOutOfRangeException(nameof(seedUrl));
        }
        ArgumentNullException.ThrowIfNull(allowedPageHosts);
        ArgumentNullException.ThrowIfNull(allowedPagePathPrefixes);
        ArgumentNullException.ThrowIfNull(allowedAssetHosts);
        ArgumentNullException.ThrowIfNull(urlOptions);
        ArgumentNullException.ThrowIfNull(discoveryProfile);
        ArgumentNullException.ThrowIfNull(analysisLimits);
        ArgumentNullException.ThrowIfNull(recommendationThresholds);
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonProfile);
        if (allowedPageHosts.Count == 0 || allowedAssetHosts.Count == 0)
        {
            throw new ArgumentException("Page and asset scopes each need an allowed host.");
        }
        if (maxTotalImageBytes < analysisLimits.MaxEncodedBytes
            || maxTotalImageBytes > 1024L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalImageBytes));
        }

        EndpointId = endpointId;
        SeedUrl = seedUrl;
        IsProduction = isProduction;
        AllowedPageHosts = [.. allowedPageHosts];
        AllowedPagePathPrefixes = [.. allowedPagePathPrefixes];
        AllowedAssetHosts = [.. allowedAssetHosts];
        UrlOptions = urlOptions with
        {
            TrackingParameters = new HashSet<string>(
                urlOptions.TrackingParameters, StringComparer.OrdinalIgnoreCase),
            SensitiveQueryParameters = new HashSet<string>(
                urlOptions.SensitiveQueryParameters, StringComparer.OrdinalIgnoreCase)
        };
        DiscoveryProfile = discoveryProfile;
        AnalysisLimits = analysisLimits;
        MaxTotalImageBytes = maxTotalImageBytes;
        RecommendationThresholds = recommendationThresholds;
        AnalyzerProfile = analyzerProfile;
        ComparisonProfile = comparisonProfile;
    }

    public Guid EndpointId { get; }
    public string SeedUrl { get; }
    public bool IsProduction { get; }
    public IReadOnlyList<CrawlHostRule> AllowedPageHosts { get; }
    public IReadOnlyList<string> AllowedPagePathPrefixes { get; }
    public IReadOnlyList<CrawlHostRule> AllowedAssetHosts { get; }
    public CrawlUrlOptions UrlOptions { get; }
    public PngSiteDiscoveryProfile DiscoveryProfile { get; }
    public PngImageAnalysisLimits AnalysisLimits { get; }
    public long MaxTotalImageBytes { get; }
    public PngRecommendationThresholds RecommendationThresholds { get; }
    public string AnalyzerProfile { get; }
    public string ComparisonProfile { get; }
}

public sealed record PngAuditQueuedRun
{
    public PngAuditQueuedRun(
        Guid runId,
        string source,
        Guid? initiatedByUserId,
        PngAuditRunSnapshot snapshot,
        DateTimeOffset queuedAt)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A PNG audit run needs an identifier.", nameof(runId));
        }
        if (source is not PngAuditSources.Manual and not PngAuditSources.Scheduled)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        if (initiatedByUserId == Guid.Empty)
        {
            throw new ArgumentException("An initiating user identifier cannot be empty.", nameof(initiatedByUserId));
        }
        ArgumentNullException.ThrowIfNull(snapshot);

        RunId = runId;
        Source = source;
        InitiatedByUserId = initiatedByUserId;
        Snapshot = snapshot;
        QueuedAt = queuedAt;
    }

    public Guid RunId { get; }
    public string Source { get; }
    public Guid? InitiatedByUserId { get; }
    public PngAuditRunSnapshot Snapshot { get; }
    public DateTimeOffset QueuedAt { get; }
}

public sealed record PngAuditRunClaim(
    Guid RunId,
    Guid LeaseToken,
    int AttemptCount,
    PngAuditRunSnapshot Snapshot,
    string SeedIdentityHash,
    DateTimeOffset QueuedAt,
    DateTimeOffset StartedAt);

public sealed record PngAuditImageIdentity
{
    public PngAuditImageIdentity(string displayUrl, string identityHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityHash);
        if (displayUrl.Length > CrawlUrlOptions.MaxUrlLength)
        {
            throw new ArgumentOutOfRangeException(nameof(displayUrl));
        }
        if (identityHash.Length != 64 || !identityHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A PNG image identity must be a hexadecimal SHA-256 value.",
                nameof(identityHash));
        }

        DisplayUrl = displayUrl;
        IdentityHash = identityHash.ToLowerInvariant();
    }

    public string DisplayUrl { get; }
    public string IdentityHash { get; }
}

public sealed record PngAuditImageRecord
{
    private PngAuditImageRecord(
        PngAuditImageIdentity image,
        string classification,
        string? reasonCode,
        string? finalDisplayUrl,
        string? finalIdentityHash,
        string? declaredContentType,
        int? httpStatusCode,
        long responseBytes,
        string? detectedFormat,
        PngImageFacts? facts,
        PngComparisonMetrics? comparison,
        string recommendation,
        string? suggestedFormat)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(classification);
        ArgumentOutOfRangeException.ThrowIfNegative(responseBytes);
        if ((finalDisplayUrl is null) != (finalIdentityHash is null))
        {
            throw new ArgumentException("The final image URL and identity must be supplied together.");
        }
        if (finalDisplayUrl is not null)
        {
            _ = new PngAuditImageIdentity(finalDisplayUrl, finalIdentityHash!);
        }

        Image = image;
        Classification = classification;
        ReasonCode = reasonCode;
        FinalDisplayUrl = finalDisplayUrl;
        FinalIdentityHash = finalIdentityHash;
        DeclaredContentType = declaredContentType;
        HttpStatusCode = httpStatusCode;
        ResponseBytes = responseBytes;
        DetectedFormat = detectedFormat;
        Facts = facts;
        Comparison = comparison;
        Recommendation = recommendation;
        SuggestedFormat = suggestedFormat;
    }

    public PngAuditImageIdentity Image { get; }
    public string Classification { get; }
    public string? ReasonCode { get; }
    public string? FinalDisplayUrl { get; }
    public string? FinalIdentityHash { get; }
    public string? DeclaredContentType { get; }
    public int? HttpStatusCode { get; }
    public long ResponseBytes { get; }
    public string? DetectedFormat { get; }
    public PngImageFacts? Facts { get; }
    public PngComparisonMetrics? Comparison { get; }
    public string Recommendation { get; }
    public string? SuggestedFormat { get; }

    public static PngAuditImageRecord FetchFailed(
        PngAuditImageIdentity image,
        string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return new(image, PngAuditImageClassifications.FetchFailed, reasonCode, null, null, null,
            null, 0, null, null, null, PngAuditRecommendations.None, null);
    }

    public static PngAuditImageRecord ProcessingIncomplete(
        PngAuditImageIdentity image,
        string reasonCode,
        string finalDisplayUrl,
        string finalIdentityHash,
        string? declaredContentType,
        long responseBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentOutOfRangeException.ThrowIfNegative(responseBytes);
        return new(image, PngAuditImageClassifications.FetchFailed, reasonCode,
            finalDisplayUrl, finalIdentityHash, declaredContentType, null,
            responseBytes, null, null, null, PngAuditRecommendations.None, null);
    }

    public static PngAuditImageRecord HttpNonSuccess(
        PngAuditImageIdentity image,
        string finalDisplayUrl,
        string finalIdentityHash,
        string? declaredContentType,
        int httpStatusCode,
        long responseBytes)
    {
        if (httpStatusCode is < 100 or > 599 or >= 200 and <= 299)
        {
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
        }

        return new(image, PngAuditImageClassifications.HttpNonSuccess, null,
            finalDisplayUrl, finalIdentityHash, declaredContentType, httpStatusCode,
            responseBytes, null, null, null, PngAuditRecommendations.None, null);
    }

    public static PngAuditImageRecord ResponseTruncated(
        PngAuditImageIdentity image,
        string finalDisplayUrl,
        string finalIdentityHash,
        string? declaredContentType,
        int httpStatusCode,
        long responseBytes)
    {
        if (httpStatusCode is < 200 or > 299)
        {
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(responseBytes);
        return new(image, PngAuditImageClassifications.ResponseTruncated, null,
            finalDisplayUrl, finalIdentityHash, declaredContentType, httpStatusCode,
            responseBytes, null, null, null, PngAuditRecommendations.None, null);
    }

    public static PngAuditImageRecord Analyzed(
        PngAuditImageIdentity image,
        string finalDisplayUrl,
        string finalIdentityHash,
        string? declaredContentType,
        int httpStatusCode,
        PngAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (httpStatusCode is < 200 or > 299)
        {
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
        }

        var recommendation = analysis.Recommendation == PngRecommendation.LosslessWebp
            ? PngAuditRecommendations.LosslessWebp
            : PngAuditRecommendations.None;
        return new(
            image,
            analysis.Classification.ToString(),
            null,
            finalDisplayUrl,
            finalIdentityHash,
            declaredContentType,
            httpStatusCode,
            analysis.OriginalBytes,
            analysis.DetectedFormat,
            analysis.Image,
            analysis.Comparison,
            recommendation,
            recommendation == PngAuditRecommendations.LosslessWebp ? "WebP" : null);
    }
}

public sealed record PngAuditCrawlProgress
{
    public PngAuditCrawlProgress(int pagesDiscovered, int httpAttempts, long totalPageBytes)
    {
        if (pagesDiscovered < 0 || httpAttempts < 0 || totalPageBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pagesDiscovered));
        }

        PagesDiscovered = pagesDiscovered;
        HttpAttempts = httpAttempts;
        TotalPageBytes = totalPageBytes;
    }

    public int PagesDiscovered { get; }
    public int HttpAttempts { get; }
    public long TotalPageBytes { get; }
}

public sealed record PngAuditResultBatch
{
    public PngAuditResultBatch(
        IReadOnlyList<PngAuditImageRecord> images,
        IReadOnlyList<PngImageSourceMapping> sourceMappings,
        IReadOnlyList<PngDiscoverySkip> discoverySkips,
        IReadOnlyList<PngCoverageReason> coverageReasons,
        PngAuditCrawlProgress crawlProgress)
    {
        ArgumentNullException.ThrowIfNull(crawlProgress);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(sourceMappings);
        ArgumentNullException.ThrowIfNull(discoverySkips);
        ArgumentNullException.ThrowIfNull(coverageReasons);
        if (images.Any(image => image is null)
            || sourceMappings.Any(mapping => mapping is null)
            || discoverySkips.Any(skip => skip is null)
            || coverageReasons.Any(reason => reason is null))
        {
            throw new ArgumentException("PNG audit result batches cannot contain null records.");
        }

        Images = [.. images];
        SourceMappings = [.. sourceMappings];
        DiscoverySkips = [.. discoverySkips];
        CoverageReasons = [.. coverageReasons];
        CrawlProgress = crawlProgress;
    }

    public IReadOnlyList<PngAuditImageRecord> Images { get; }
    public IReadOnlyList<PngImageSourceMapping> SourceMappings { get; }
    public IReadOnlyList<PngDiscoverySkip> DiscoverySkips { get; }
    public IReadOnlyList<PngCoverageReason> CoverageReasons { get; }
    public PngAuditCrawlProgress CrawlProgress { get; }
}

public sealed record PngAuditRunTotals
{
    public PngAuditRunTotals(
        int pagesDiscovered,
        int imagesDiscovered,
        int imagesAnalyzed,
        int recommendationCount,
        int discoverySkipCount,
        int httpAttempts,
        long totalPageBytes,
        long totalImageBytes,
        bool crawlCoverageLimited,
        bool imageAnalysisCoverageLimited,
        bool sourceMappingCoverageLimited)
    {
        if (pagesDiscovered < 0
            || imagesDiscovered < 0
            || imagesAnalyzed < 0
            || imagesAnalyzed > imagesDiscovered
            || recommendationCount < 0
            || recommendationCount > imagesAnalyzed
            || discoverySkipCount < 0
            || httpAttempts < 0
            || totalPageBytes < 0
            || totalImageBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pagesDiscovered));
        }

        PagesDiscovered = pagesDiscovered;
        ImagesDiscovered = imagesDiscovered;
        ImagesAnalyzed = imagesAnalyzed;
        RecommendationCount = recommendationCount;
        DiscoverySkipCount = discoverySkipCount;
        HttpAttempts = httpAttempts;
        TotalPageBytes = totalPageBytes;
        TotalImageBytes = totalImageBytes;
        CrawlCoverageLimited = crawlCoverageLimited;
        ImageAnalysisCoverageLimited = imageAnalysisCoverageLimited;
        SourceMappingCoverageLimited = sourceMappingCoverageLimited;
    }

    public int PagesDiscovered { get; }
    public int ImagesDiscovered { get; }
    public int ImagesAnalyzed { get; }
    public int RecommendationCount { get; }
    public int DiscoverySkipCount { get; }
    public int HttpAttempts { get; }
    public long TotalPageBytes { get; }
    public long TotalImageBytes { get; }
    public bool CrawlCoverageLimited { get; }
    public bool ImageAnalysisCoverageLimited { get; }
    public bool SourceMappingCoverageLimited { get; }
}

public interface IPngAuditResultSink
{
    Task CreateQueuedRunAsync(
        PngAuditQueuedRun run,
        CancellationToken cancellationToken = default);

    Task<PngAuditRunClaim?> TryClaimAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(
        Guid runId,
        Guid leaseToken,
        CancellationToken cancellationToken = default);

    Task<bool> RecordBatchAsync(
        Guid runId,
        Guid leaseToken,
        PngAuditResultBatch batch,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        Guid runId,
        Guid leaseToken,
        PngAuditRunTotals totals,
        CancellationToken cancellationToken = default);

    Task<bool> FailAsync(
        Guid runId,
        Guid? leaseToken,
        string status,
        string failureCode,
        string? safeDiagnostic,
        CancellationToken cancellationToken = default);
}

public sealed record PngAuditRunView(
    Guid RunId,
    Guid EndpointId,
    string Source,
    string Status,
    string SeedUrl,
    int AttemptCount,
    int PagesDiscovered,
    int ImagesDiscovered,
    int ImagesAnalyzed,
    int RecommendationCount,
    int DiscoverySkipCount,
    bool CrawlCoverageLimited,
    bool ImageAnalysisCoverageLimited,
    bool SourceMappingCoverageLimited,
    string? FailureCode,
    string? SafeDiagnostic,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record PngAuditImageResultView(
    Guid ImageResultId,
    string ImageDisplayUrl,
    string? FinalDisplayUrl,
    string Classification,
    string? ReasonCode,
    int? HttpStatusCode,
    long ResponseBytes,
    int? Width,
    int? Height,
    int? FrameCount,
    bool? UsesTransparency,
    string Recommendation,
    long? CandidateWebpBytes,
    long? OriginalSavingsBytes,
    decimal? OriginalSavingsPercent,
    long? NormalizedSavingsBytes,
    decimal? NormalizedSavingsPercent,
    DateTimeOffset RecordedAt);

public sealed record PngAuditImageSourceView(
    Guid SourceId,
    string SourcePageDisplayUrl,
    string AttributeKind,
    string? Descriptor);

public sealed record PngAuditDiscoverySkipView(
    Guid SkipId,
    string SourcePageDisplayUrl,
    string AttributeKind,
    string? Descriptor,
    string BoundedSafeRawValue,
    string ReasonCode,
    DateTimeOffset RecordedAt);

public sealed record PngAuditCoverageReasonView(string Area, string ReasonCode, int Count);

public sealed record PngAuditPage<T>(IReadOnlyList<T> Items, int Offset, int Limit, bool HasMore);

public interface IPngAuditReader
{
    Task<PngAuditRunView?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PngAuditRunView>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<PngAuditPage<PngAuditImageResultView>> ListImagesAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<PngAuditPage<PngAuditImageSourceView>> ListSourcesAsync(
        Guid imageResultId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<PngAuditPage<PngAuditDiscoverySkipView>> ListDiscoverySkipsAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PngAuditCoverageReasonView>> ListCoverageReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public interface IPngAuditReconciler
{
    Task<IReadOnlyList<Guid>> ReconcileAsync(
        CancellationToken cancellationToken = default);
}

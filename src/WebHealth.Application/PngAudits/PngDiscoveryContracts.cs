using WebHealth.Application.Monitoring;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Domain.Crawling;

namespace WebHealth.Application.PngAudits;

public interface IPngSiteCrawler
{
    Task<PngSiteDiscoveryResult> DiscoverAsync(
        PngSiteCrawlRequest request,
        CancellationToken cancellationToken = default);
}

public static class PngAuditFetchRetry
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
}

public sealed record PngSiteCrawlRequest
{
    public PngSiteCrawlRequest(
        Guid runId,
        Guid endpointId,
        bool isProduction,
        PngSiteDiscoveryScope scope,
        PngSiteDiscoveryProfile profile)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A PNG discovery run needs an identifier.", nameof(runId));
        }
        if (endpointId == Guid.Empty)
        {
            throw new ArgumentException("A PNG discovery run needs an endpoint identifier.", nameof(endpointId));
        }
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(profile);
        RunId = runId;
        EndpointId = endpointId;
        IsProduction = isProduction;
        Scope = scope;
        Profile = profile;
    }

    public Guid RunId { get; }

    public Guid EndpointId { get; }

    public bool IsProduction { get; }

    public PngSiteDiscoveryScope Scope { get; }

    public PngSiteDiscoveryProfile Profile { get; }

    public int ConsumedHttpAttempts { get; init; }

    public long ConsumedPageBytes { get; init; }

    public DateTimeOffset? RunDeadline { get; init; }
}

public sealed record PngSiteDiscoveryScope
{
    public PngSiteDiscoveryScope(
        string seedUrl,
        IReadOnlyList<CrawlHostRule> allowedPageHosts,
        IReadOnlyList<string> allowedPagePathPrefixes,
        IReadOnlyList<CrawlHostRule> allowedAssetHosts,
        CrawlUrlOptions urlOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedUrl);
        ArgumentNullException.ThrowIfNull(allowedPageHosts);
        ArgumentNullException.ThrowIfNull(allowedPagePathPrefixes);
        ArgumentNullException.ThrowIfNull(allowedAssetHosts);
        ArgumentNullException.ThrowIfNull(urlOptions);
        if (allowedPageHosts.Count == 0 || allowedAssetHosts.Count == 0)
        {
            throw new ArgumentException("Page and asset scopes each need at least one allowed host.");
        }
        if (allowedPageHosts.Concat(allowedAssetHosts)
            .Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Host)))
        {
            throw new ArgumentException("Allowed hosts cannot be empty.");
        }
        if (allowedPagePathPrefixes.Any(prefix =>
            string.IsNullOrWhiteSpace(prefix)
            || !prefix.StartsWith("/", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Allowed page path prefixes must be absolute paths.");
        }
        if (!CrawlUrlNormalizer.Normalize(seedUrl, urlOptions).Succeeded)
        {
            throw new ArgumentException("The PNG discovery seed URL is invalid.", nameof(seedUrl));
        }

        SeedUrl = seedUrl;
        AllowedPageHosts = [.. allowedPageHosts];
        AllowedPagePathPrefixes = [.. allowedPagePathPrefixes];
        AllowedAssetHosts = [.. allowedAssetHosts];
        UrlOptions = urlOptions;
    }

    public string SeedUrl { get; }

    public IReadOnlyList<CrawlHostRule> AllowedPageHosts { get; }

    public IReadOnlyList<string> AllowedPagePathPrefixes { get; }

    public IReadOnlyList<CrawlHostRule> AllowedAssetHosts { get; }

    public CrawlUrlOptions UrlOptions { get; }
}

public sealed record PngSiteDiscoveryProfile
{
    public PngSiteDiscoveryProfile(
        PngPageDiscoveryLimits pages,
        PngImageDiscoveryLimits images,
        PngDiscoveryFetchPolicy fetch)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(fetch);
        Pages = pages;
        Images = images;
        Fetch = fetch;
    }

    public PngPageDiscoveryLimits Pages { get; }

    public PngImageDiscoveryLimits Images { get; }

    public PngDiscoveryFetchPolicy Fetch { get; }
}

public sealed record PngPageDiscoveryLimits
{
    public PngPageDiscoveryLimits(
        int maxPages,
        int maxDepth,
        int maxPageBytes,
        long maxTotalPageBytes,
        int maxImageReferencesPerPage)
    {
        if (maxPages is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        }
        if (maxDepth is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }
        if (maxPageBytes is < 1 or > SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPageBytes));
        }
        if (maxTotalPageBytes < maxPageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalPageBytes));
        }
        if (maxTotalPageBytes > 512L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalPageBytes));
        }
        if (maxImageReferencesPerPage is < 1
            or > HtmlDocumentDiscoveryLimits.MaxImageReferences)
        {
            throw new ArgumentOutOfRangeException(nameof(maxImageReferencesPerPage));
        }
        MaxPages = maxPages;
        MaxDepth = maxDepth;
        MaxPageBytes = maxPageBytes;
        MaxTotalPageBytes = maxTotalPageBytes;
        MaxImageReferencesPerPage = maxImageReferencesPerPage;
    }

    public int MaxPages { get; }

    public int MaxDepth { get; }

    public int MaxPageBytes { get; }

    public long MaxTotalPageBytes { get; }

    public int MaxImageReferencesPerPage { get; }
}

public sealed record PngImageDiscoveryLimits
{
    public PngImageDiscoveryLimits(int maxUniqueImages, int maxTotalSourceMappings)
    {
        if (maxUniqueImages is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUniqueImages));
        }
        if (maxTotalSourceMappings < maxUniqueImages)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalSourceMappings));
        }
        if (maxTotalSourceMappings > 50000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalSourceMappings));
        }
        MaxUniqueImages = maxUniqueImages;
        MaxTotalSourceMappings = maxTotalSourceMappings;
    }

    public int MaxUniqueImages { get; }

    public int MaxTotalSourceMappings { get; }
}

public sealed record PngDiscoveryFetchPolicy
{
    public PngDiscoveryFetchPolicy(
        int maxTotalHttpAttempts,
        int timeoutSeconds,
        double requestsPerSecondPerHost,
        int transientRetryCount,
        TimeSpan maxDuration)
    {
        if (maxTotalHttpAttempts is < 1 or > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalHttpAttempts));
        }
        if (timeoutSeconds is < 1 or > SafeHttpTransportDefaults.MaxTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }
        if (!double.IsFinite(requestsPerSecondPerHost)
            || requestsPerSecondPerHost is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(requestsPerSecondPerHost));
        }
        if (transientRetryCount is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(transientRetryCount));
        }
        if (maxDuration <= TimeSpan.Zero || maxDuration > TimeSpan.FromHours(4))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDuration));
        }
        MaxTotalHttpAttempts = maxTotalHttpAttempts;
        TimeoutSeconds = timeoutSeconds;
        RequestsPerSecondPerHost = requestsPerSecondPerHost;
        TransientRetryCount = transientRetryCount;
        MaxDuration = maxDuration;
    }

    public int MaxTotalHttpAttempts { get; }

    public int TimeoutSeconds { get; }

    public double RequestsPerSecondPerHost { get; }

    public int TransientRetryCount { get; }

    public TimeSpan MaxDuration { get; }
}

public sealed record PngDiscoveredPage(
    string DisplayUrl,
    string IdentityHash,
    int Depth);

public sealed record PngDiscoveredImageRequest(
    string FetchUrl,
    string DisplayUrl,
    string IdentityHash);

public sealed record PngImageSourceMapping(
    string ImageIdentityHash,
    string SourcePageDisplayUrl,
    string SourcePageIdentityHash,
    string AttributeKind,
    string? Descriptor);

public sealed record PngDiscoverySkip(
    string SourcePageDisplayUrl,
    string SourcePageIdentityHash,
    string AttributeKind,
    string? Descriptor,
    string SafeRawValue,
    PngDiscoverySkipReason Reason);

public enum PngDiscoverySkipReason
{
    DataUrl,
    BlobUrl,
    UnsupportedScheme,
    CredentialsPresent,
    MalformedUrl,
    OverlongValue,
    ExternalAssetHost,
    ReferenceLimit,
    UniqueImageLimit,
    SourceMappingLimit
}

public enum PngCoverageArea
{
    Crawl,
    ImageAnalysis,
    SourceMappings
}

public enum PngCoverageReasonCode
{
    PageLimit,
    DepthLimit,
    PageBodyTruncated,
    NavigationReferenceLimit,
    ImageReferenceLimit,
    UniqueImageLimit,
    TotalPageBytesLimit,
    TotalImageBytesLimit,
    SourceMappingLimit,
    RobotsDisallowed,
    DurationLimit,
    HttpAttemptLimit,
    PageFetchFailed,
    PageHttpNonSuccess,
    RedirectOutOfScope,
    DocumentNotInspected,
    QueryVariantLimit
}

public sealed record PngCoverageReason(
    PngCoverageArea Area,
    PngCoverageReasonCode Reason,
    int Count);

public sealed record PngSiteDiscoveryResult(
    IReadOnlyList<PngDiscoveredPage> Pages,
    IReadOnlyList<PngDiscoveredImageRequest> Images,
    IReadOnlyList<PngImageSourceMapping> SourceMappings,
    IReadOnlyList<PngDiscoverySkip> Skips,
    IReadOnlyList<PngCoverageReason> CoverageReasons,
    int HttpAttempts,
    long TotalPageBytes);

using WebHealth.Application.Monitoring;
using WebHealth.Domain.Normalization;

namespace WebHealth.Application.SiteAnalysis;

public interface ISiteAnalysisFetcher
{
    Task<SiteAnalysisFetchResult> FetchAsync(
        SiteAnalysisFetchRequest request,
        SiteAnalysisFetchProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record SiteAnalysisFetchRequest
{
    public SiteAnalysisFetchRequest(
        Guid executionId,
        Guid endpointId,
        string url,
        bool isProduction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var normalized = EndpointUrlNormalizer.Normalize(url);
        if (!normalized.Succeeded)
        {
            throw new ArgumentException("The URL must be an absolute HTTP or HTTPS target.", nameof(url));
        }

        ExecutionId = executionId;
        EndpointId = endpointId;
        Url = url;
        Host = normalized.NormalizedHost!;
        IsProduction = isProduction;
    }

    public Guid ExecutionId { get; }

    public Guid EndpointId { get; }

    public string Url { get; }

    public string Host { get; }

    public bool IsProduction { get; }

    public ISafeHttpRequestHopPolicy? HopPolicy { get; init; }
}

public sealed record SiteAnalysisFetchProfile
{
    public SiteAnalysisFetchProfile(
        int maxResponseBodyBytes,
        int timeoutSeconds,
        double requestsPerSecondPerHost,
        int transientRetryCount,
        TimeSpan retryBaseDelay,
        TimeSpan maxRetryDelay)
    {
        if (maxResponseBodyBytes is <= 0 or > SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBodyBytes));
        }
        if (timeoutSeconds is <= 0 or > SafeHttpTransportDefaults.MaxTimeoutSeconds)
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
        if (retryBaseDelay < TimeSpan.Zero || retryBaseDelay > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(nameof(retryBaseDelay));
        }
        if (maxRetryDelay < retryBaseDelay || maxRetryDelay > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryDelay));
        }

        MaxResponseBodyBytes = maxResponseBodyBytes;
        TimeoutSeconds = timeoutSeconds;
        RequestsPerSecondPerHost = requestsPerSecondPerHost;
        TransientRetryCount = transientRetryCount;
        RetryBaseDelay = retryBaseDelay;
        MaxRetryDelay = maxRetryDelay;
    }

    public int MaxResponseBodyBytes { get; }

    public int TimeoutSeconds { get; }

    public double RequestsPerSecondPerHost { get; }

    public int TransientRetryCount { get; }

    public TimeSpan RetryBaseDelay { get; }

    public TimeSpan MaxRetryDelay { get; }
}

public sealed record SiteAnalysisFetchResult
{
    public SiteAnalysisFetchResult(SafeHttpTransportResult response, int attempts)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        Response = response;
        Attempts = attempts;
    }

    public SafeHttpTransportResult Response { get; }

    public int Attempts { get; }
}

public sealed record HtmlDocumentDiscovery(
    IReadOnlyList<string> NavigationHrefs,
    IReadOnlyList<HtmlImageReference> Images,
    string? BaseHref,
    bool NavigationFullyInspected,
    bool ImageReferencesFullyInspected)
{
    public static HtmlDocumentDiscovery NotInspected { get; } = new([], [], null, false, false);

    public static HtmlDocumentDiscovery Nothing { get; } = new([], [], null, true, true);
}

public sealed record HtmlImageReference(
    string RawUrl,
    string AttributeKind,
    string? Descriptor);

public static class HtmlImageAttributeKinds
{
    public const string ImageSource = "img.src";
    public const string ImageSourceSet = "img.srcset";
    public const string PictureSourceSet = "source.srcset";
}

public static class HtmlDocumentDiscoveryLimits
{
    public const int MaxNavigationHrefs = 5000;
    public const int MaxImageReferences = 10000;
}

public interface IHtmlDocumentDiscoveryExtractor
{
    HtmlDocumentDiscovery Extract(
        ReadOnlyMemory<byte> body,
        string? contentType);
}

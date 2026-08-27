using WebHealth.Application.Monitoring;

namespace WebHealth.Application.PngAudits;

public interface IPngImageTransport
{
    Task<SafeHttpTransportResult> SendAsync(
        PngImageTransportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PngImageTransportRequest(
    Guid EndpointId,
    string Url,
    bool IsProduction,
    int MaxRedirects = SafeHttpTransportDefaults.MaxRedirects,
    int MaxResponseBodyBytes = SafeHttpTransportDefaults.AbsoluteMaxResponseBodyBytes,
    int TimeoutSeconds = SafeHttpTransportDefaults.DefaultTimeoutSeconds)
{
    public ISafeHttpRequestHopPolicy? HopPolicy { get; init; }

    public double RequestsPerSecondPerHost { get; init; } = 1;

    public int TransientRetryCount { get; init; }

    public int MaxOutboundRequests { get; init; } = SafeHttpTransportDefaults.MaxRedirects + 1;
}

public enum PngImageFetchClassification
{
    FetchFailed,
    HttpNonSuccess,
    ResponseTruncated
}

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
    int TimeoutSeconds = SafeHttpTransportDefaults.DefaultTimeoutSeconds,
    double RequestsPerSecondPerHost = 1)
{
    public ISafeHttpRequestHopPolicy? HopPolicy { get; init; }
}

public enum PngImageFetchClassification
{
    FetchFailed,
    HttpNonSuccess,
    ResponseTruncated
}

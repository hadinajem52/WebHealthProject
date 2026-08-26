using WebHealth.Application.Monitoring;

namespace WebHealth.Application.PngAudits;

public interface IPngImageTransport
{
    Task<SafeHttpTransportResult> SendAsync(
        SafeHttpTransportRequest request,
        CancellationToken cancellationToken = default);
}

public enum PngImageFetchClassification
{
    FetchFailed,
    HttpNonSuccess,
    ResponseTruncated
}

using WebHealth.Application.Monitoring;
using WebHealth.Application.PngAudits;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngImageTransport(
    SafeHttpTransport transport) : IPngImageTransport
{
    public Task<SafeHttpTransportResult> SendAsync(
        PngImageTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return transport.SendExtendedAsync(
            new(
                request.EndpointId,
                request.Url,
                request.IsProduction,
                request.MaxRedirects,
                request.MaxResponseBodyBytes,
                request.TimeoutSeconds)
            {
                HopPolicy = request.HopPolicy
            },
            cancellationToken);
    }
}

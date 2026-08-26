using System.Net;
using System.Net.Http;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EndpointUrlSchemeProbe(IHttpClientFactory httpClientFactory) : IEndpointUrlSchemeProbe
{
    private const int TimeoutSeconds = 5;

    public async Task<EndpointSchemeProbeOutcome> ProbeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var normalized = EndpointUrlNormalizer.Normalize(url);
        if (!normalized.Succeeded
            || DestinationHostPolicy.IsDefinitelyUnreachable(normalized.NormalizedHost, out _))
        {
            return EndpointSchemeProbeOutcome.HostUnavailable;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            using var client = httpClientFactory.CreateClient(SafeHttpTransportOptions.ClientName);
            using var message = new HttpRequestMessage(HttpMethod.Get, normalized.NormalizedUrl!);
            message.Version = HttpVersion.Version11;
            message.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            message.Headers.ConnectionClose = true;
            using var response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return EndpointSchemeProbeOutcome.Responded;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EndpointSchemeProbeOutcome.HostUnavailable;
        }
        catch (SafeDestinationException)
        {
            return EndpointSchemeProbeOutcome.HostUnavailable;
        }
        catch (HttpRequestException error)
        {
            return error.HttpRequestError == HttpRequestError.NameResolutionError
                ? EndpointSchemeProbeOutcome.HostUnavailable
                : EndpointSchemeProbeOutcome.SchemeUnavailable;
        }
        catch (IOException)
        {
            return EndpointSchemeProbeOutcome.SchemeUnavailable;
        }
    }
}

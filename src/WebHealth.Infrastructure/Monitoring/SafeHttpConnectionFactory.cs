using System.Net;
using System.Net.Http;
using System.Net.Security;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal static class SafeHttpConnectionFactory
{
    public static SocketsHttpHandler Create(
        IMonitoringDnsResolver resolver,
        IDestinationAddressPolicy addressPolicy,
        SafeHttpConcurrencyLimiter limiter,
        SafeHttpTransportOptions options) =>
        new()
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = options.ConnectTimeout,
            MaxResponseHeadersLength = options.MaxResponseHeadersKilobytes,
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            PlaintextStreamFilter = (context, _) =>
            {
                if (context.InitialRequestMessage.RequestUri?.Scheme == Uri.UriSchemeHttps)
                {
                    RecordTlsDuration(context.InitialRequestMessage);
                    RecordNegotiatedCertificate(context.InitialRequestMessage, context.PlaintextStream);
                }

                return ValueTask.FromResult(context.PlaintextStream);
            },
            ConnectCallback = (context, cancellationToken) =>
            {
                var timing = context.InitialRequestMessage.Options
                    .TryGetValue(SafeHttpTimingOptions.Key, out var collector)
                        ? collector
                        : null;

                return new ValueTask<Stream>(SafeDestinationConnector.ConnectAsync(
                    resolver,
                    addressPolicy,
                    limiter,
                    options,
                    context.DnsEndPoint.Host,
                    context.DnsEndPoint.Port,
                    timing,
                    cancellationToken));
            }
        };

    private static void RecordTlsDuration(HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(SafeHttpTimingOptions.Key, out var timing)
            && timing.ConnectCompletedTimestamp is { } connectCompletedAt)
        {
            timing.TlsDurationMs = SafeHttpTimingMath.ElapsedMs(connectCompletedAt);
        }
    }

    private static void RecordNegotiatedCertificate(HttpRequestMessage request, Stream plaintextStream)
    {
        if (plaintextStream is SslStream { RemoteCertificate: { } negotiated }
            && request.Options.TryGetValue(SafeHttpTlsOptions.Key, out var tls))
        {
            tls.CertificateDer = negotiated.GetRawCertData();
        }
    }
}

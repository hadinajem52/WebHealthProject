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
            // Fires once the connection is ready to carry HTTP: right after TCP connect for
            // plain HTTP, or right after the TLS handshake completes for HTTPS. Unlike
            // ConnectCallback, its context carries the same InitialRequestMessage, so this is
            // the correlated hook for "handshake done" timing rather than a global callback.
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

    /// <summary>
    /// This filter only runs after a fully validated handshake, so the certificate recorded
    /// here is by definition trusted and hostname-matched: the availability path never sees a
    /// rejected certificate, and certificate validation stays untouched (BR-Q04). Evidence for
    /// invalid certificates comes from <see cref="SslCertificateProbe" /> instead.
    /// </summary>
    private static void RecordNegotiatedCertificate(HttpRequestMessage request, Stream plaintextStream)
    {
        if (plaintextStream is SslStream { RemoteCertificate: { } negotiated }
            && request.Options.TryGetValue(SafeHttpTlsOptions.Key, out var tls))
        {
            // Copy the encoded certificate now: the instance is owned by the SslStream and is
            // disposed with the connection, well before the result is assembled.
            tls.CertificateDer = negotiated.GetRawCertData();
        }
    }
}

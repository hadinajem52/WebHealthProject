using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Normalization;

namespace WebHealth.Infrastructure.Monitoring;

/// <summary>
/// Opens a TLS connection purely to observe the certificate, then tears it down.
///
/// The validation callback records the presented certificate and its policy errors and always
/// returns <c>false</c>. That is deliberate and is the only way to satisfy BR-C03 — reporting
/// expired, not-yet-valid, hostname-mismatched and untrusted certificates requires seeing a
/// certificate the platform rejects — without ever accepting one (BR-Q04). Because the
/// callback always fails the handshake, no session key is ever used and no application data is
/// sent or received on a probe connection.
/// </summary>
internal sealed class SslCertificateProbe(
    IMonitoringDnsResolver resolver,
    IDestinationAddressPolicy addressPolicy,
    IMonitoringTargetAuthorizer targetAuthorizer,
    SafeHttpConcurrencyLimiter concurrencyLimiter,
    SafeHttpTransportOptions options,
    TimeProvider timeProvider) : ISslCertificateProbe
{
    public async Task<SslCertificateProbeResult> ProbeAsync(
        SslCertificateProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        if (!normalized.Succeeded
            || request.TimeoutSeconds <= 0
            || request.TimeoutSeconds > SafeHttpTransportDefaults.MaxTimeoutSeconds)
        {
            return Failure(SslProbeFailureKind.InvalidUrl, stopwatch);
        }

        var target = new Uri(normalized.NormalizedUrl!, UriKind.Absolute);
        if (target.Scheme != Uri.UriSchemeHttps)
        {
            // BR-C01: HTTP-only endpoints have no certificate to inspect and are reported as
            // Not Applicable rather than probed.
            return Failure(SslProbeFailureKind.NotHttps, stopwatch);
        }

        var host = normalized.NormalizedHost!;
        var port = normalized.EffectivePort!.Value;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            using var globalLease = await concurrencyLimiter.AcquireGlobalAsync(timeout.Token);
            if (!await targetAuthorizer.IsAuthorizedAsync(
                request.EndpointId, host, port, timeProvider.GetUtcNow(), timeout.Token))
            {
                return Failure(SslProbeFailureKind.TargetNotAuthorized, stopwatch);
            }

            using var hostLease = await concurrencyLimiter.AcquireHostAsync(host, timeout.Token);
            await using var connection = await SafeDestinationConnector.ConnectAsync(
                resolver, addressPolicy, concurrencyLimiter, options, host, port, null, timeout.Token);

            var inspection = new CertificateInspection();
            await using var ssl = new SslStream(
                connection,
                leaveInnerStreamOpen: false,
                inspection.RecordAndReject);

            try
            {
                await ssl.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = host },
                    timeout.Token);
            }
            catch (Exception exception) when (IsHandshakeFailure(exception, timeout))
            {
                // Every probe ends here: the callback rejects the certificate by design, and a
                // target that refuses or drops the handshake fails here too. The connection
                // itself already succeeded, so what happened next is handshake evidence —
                // useful when a certificate was captured, and a failure when none was.
            }

            var observation = TlsCertificateReader.TryRead(
                inspection.CertificateDer,
                inspection.HostnameMatched,
                inspection.ChainTrusted,
                timeProvider.GetUtcNow());

            return observation is null
                ? Failure(SslProbeFailureKind.HandshakeFailed, stopwatch)
                : new SslCertificateProbeResult(null, observation, stopwatch.Elapsed);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(SslProbeFailureKind.Cancelled, stopwatch);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException
            && timeout.IsCancellationRequested)
        {
            return Failure(SslProbeFailureKind.Timeout, stopwatch);
        }
        catch (SafeDestinationException)
        {
            return Failure(SslProbeFailureKind.DestinationRejected, stopwatch);
        }
        catch (SocketException exception)
        {
            return Failure(
                exception.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
                    ? SslProbeFailureKind.NameResolution
                    : SslProbeFailureKind.Connection,
                stopwatch);
        }
        catch (IOException)
        {
            return Failure(SslProbeFailureKind.Connection, stopwatch);
        }
    }

    /// <summary>
    /// A failure after the socket is connected belongs to the handshake, whatever type the
    /// platform surfaces it as — a refused handshake arrives as an authentication error on one
    /// platform and as a dropped stream on another. Cancellation and timeout keep their own
    /// meaning and are re-thrown to the outer handlers.
    /// </summary>
    private static bool IsHandshakeFailure(Exception exception, CancellationTokenSource timeout) =>
        exception is AuthenticationException or IOException or SocketException
        && !timeout.IsCancellationRequested;

    private static SslCertificateProbeResult Failure(SslProbeFailureKind failure, Stopwatch stopwatch) =>
        new(failure, null, stopwatch.Elapsed);

    private sealed class CertificateInspection
    {
        public byte[]? CertificateDer { get; private set; }
        public bool HostnameMatched { get; private set; }
        public bool ChainTrusted { get; private set; }

        public bool RecordAndReject(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors errors)
        {
            CertificateDer = certificate?.GetRawCertData();
            HostnameMatched = !errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch);
            ChainTrusted = TlsChainTrust.Evaluate(errors, TlsChainTrust.ReadElementStatuses(chain));

            // Never accept. The handshake fails here, every time, on purpose.
            return false;
        }
    }
}

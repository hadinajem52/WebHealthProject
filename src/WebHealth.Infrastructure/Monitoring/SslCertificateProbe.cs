using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Normalization;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SslCertificateProbe(
    IMonitoringDnsResolver resolver,
    IDestinationAddressPolicy addressPolicy,
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
            return Failure(SslProbeFailureKind.NotHttps, stopwatch);
        }

        var host = normalized.NormalizedHost!;
        var port = normalized.EffectivePort!.Value;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            using var globalLease = await concurrencyLimiter.AcquireGlobalAsync(timeout.Token);
            using var hostLease = await concurrencyLimiter.AcquireHostAsync(host, timeout.Token);
            await using var connection = await SafeDestinationConnector.ConnectAsync(
                resolver, addressPolicy, concurrencyLimiter, options, host, port, null, timeout.Token,
                request.ConnectionAuthorization is { } authorization
                    ? token => authorization.IsAuthorizedAsync(request.EndpointId, host, port, token)
                    : null);

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

            return false;
        }
    }
}

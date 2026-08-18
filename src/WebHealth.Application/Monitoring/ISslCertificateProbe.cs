using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

/// <summary>
/// Inspects the certificate an HTTPS endpoint presents, without ever completing a usable TLS
/// session. The probe records the certificate and its validation errors and then rejects the
/// handshake, so evidence for expired, not-yet-valid, hostname-mismatched and untrusted
/// certificates (BR-C03) is captured while certificate validation is never bypassed (BR-Q04).
/// No application data is ever sent over a probe connection.
/// </summary>
public interface ISslCertificateProbe
{
    Task<SslCertificateProbeResult> ProbeAsync(
        SslCertificateProbeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SslCertificateProbeRequest(
    Guid EndpointId,
    string Url,
    int TimeoutSeconds = SafeHttpTransportDefaults.DefaultTimeoutSeconds);

/// <summary>
/// A probe succeeds whenever a certificate was observed, regardless of whether that
/// certificate is valid. An invalid certificate is a successful observation with a
/// non-<see cref="TlsValidationCategory.Valid" /> category, not a probe failure.
/// </summary>
public sealed record SslCertificateProbeResult(
    SslProbeFailureKind? Failure,
    TlsCertificateObservation? Certificate,
    TimeSpan Duration)
{
    public bool Succeeded => Failure is null;
}

public enum SslProbeFailureKind
{
    InvalidUrl,
    NotHttps,
    TargetNotAuthorized,
    DestinationRejected,
    NameResolution,
    Connection,

    /// <summary>
    /// The handshake ended before the peer presented a certificate this system could read —
    /// for example a protocol or cipher mismatch, or a server that closed the connection.
    /// There is no certificate to categorise, and BR-C03 treats it as a critical result.
    /// </summary>
    HandshakeFailed,
    Timeout,
    Cancelled
}

/// <summary>
/// The certificate evidence required by BR-C02. Public key material only: no private keys and
/// no raw certificate bytes are retained.
/// </summary>
public sealed record TlsCertificateObservation(
    string Subject,
    string Issuer,
    string SerialNumber,
    string Sha256Fingerprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    IReadOnlyList<string> SubjectAlternativeNames,
    bool HostnameMatched,
    bool ChainTrusted,
    TlsValidationCategory ValidationCategory,
    DateTimeOffset ObservedAt);

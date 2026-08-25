using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

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

    HandshakeFailed,
    Timeout,
    Cancelled
}

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

using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

public static class SslMonitorIdentity
{
    public const string MonitorType = "SslCertificate";
    public const string DefaultDiscriminator = "default";

    public static string CreateIssueKey(string ruleKey, string discriminator = DefaultDiscriminator) =>
        $"v1|{MonitorType}|{ruleKey}|{discriminator}";
}

/// <summary>
/// Certificate-specific result categories. Transport-level problems reuse the shared
/// categories, because a DNS or connection failure means the same thing whichever monitor hit
/// it. Expiry <em>warning bands</em> are not here: this increment records what was observed,
/// and severity boundaries belong to the SSL severity increment.
/// </summary>
public static class SslFailureCategories
{
    public const string Expired = "SslExpired";
    public const string NotYetValid = "SslNotYetValid";
    public const string HostnameMismatch = "SslHostnameMismatch";
    public const string Untrusted = "SslUntrusted";
    public const string HandshakeFailed = "SslHandshakeFailed";

    public static readonly string[] All =
        [Expired, NotYetValid, HostnameMismatch, Untrusted, HandshakeFailed];
}

public sealed record NormalizeSslResult(
    SslCertificateProbeResult Probe,
    DateTimeOffset MeasuredAt);

public static class SslResultNormalizer
{
    public const string MonitorSource = "WebHealthSslProbeV1";

    public static NormalizedCheckResult Normalize(NormalizeSslResult input)
    {
        var category = SelectFailureCategory(input.Probe);
        var outcome = SelectOutcome(input.Probe, category);
        var findings = category is null || outcome == HttpResultOutcomes.Cancelled
            ? Array.Empty<NormalizedFinding>()
            : [CreateFinding(category, input.Probe.Certificate)];

        return new(
            outcome,
            category,
            null,
            ToBoundedMilliseconds(input.Probe.Duration),
            null,
            null,
            MonitorSource,
            input.MeasuredAt,
            Diagnostic(category),
            [],
            findings);
    }

    /// <summary>
    /// A certificate that was observed but rejected is still a completed observation, so the
    /// category comes from its validation state. Only a probe that produced no certificate at
    /// all falls back to a transport-level category.
    /// </summary>
    private static string? SelectFailureCategory(SslCertificateProbeResult probe)
    {
        if (probe.Certificate is { } certificate)
        {
            return certificate.ValidationCategory switch
            {
                TlsValidationCategory.Valid => null,
                TlsValidationCategory.NotYetValid => SslFailureCategories.NotYetValid,
                TlsValidationCategory.Expired => SslFailureCategories.Expired,
                TlsValidationCategory.HostnameMismatch => SslFailureCategories.HostnameMismatch,
                _ => SslFailureCategories.Untrusted
            };
        }

        return probe.Failure switch
        {
            SslProbeFailureKind.NameResolution => HttpFailureCategories.Dns,
            SslProbeFailureKind.Connection => HttpFailureCategories.Connection,
            SslProbeFailureKind.Timeout => HttpFailureCategories.Timeout,
            SslProbeFailureKind.Cancelled => HttpFailureCategories.Cancellation,
            SslProbeFailureKind.DestinationRejected or SslProbeFailureKind.TargetNotAuthorized =>
                HttpFailureCategories.DestinationPolicy,
            SslProbeFailureKind.InvalidUrl or SslProbeFailureKind.NotHttps =>
                HttpFailureCategories.InvalidConfiguration,
            _ => SslFailureCategories.HandshakeFailed
        };
    }

    private static string SelectOutcome(SslCertificateProbeResult probe, string? category)
    {
        if (category is null)
        {
            return HttpResultOutcomes.Healthy;
        }

        return probe.Failure == SslProbeFailureKind.Cancelled
            ? HttpResultOutcomes.Cancelled
            : HttpResultOutcomes.Critical;
    }

    private static NormalizedFinding CreateFinding(string category, TlsCertificateObservation? certificate) =>
        new(
            category,
            category,
            FindingSeverities.Critical,
            certificate is null ? "No certificate was presented" : Describe(certificate),
            "A trusted certificate valid for the requested host",
            SslMonitorIdentity.CreateIssueKey(category));

    /// <summary>
    /// Observed values are bounded, non-sensitive certificate facts only. Fingerprints are
    /// public data; no private key material exists in the observation to leak.
    /// </summary>
    private static string Describe(TlsCertificateObservation certificate) =>
        Bounded($"{certificate.ValidationCategory}; expires {certificate.NotAfter:yyyy-MM-dd}");

    private static string? Diagnostic(string? category) => category switch
    {
        null => null,
        SslFailureCategories.Expired => "The presented certificate is past its validity period.",
        SslFailureCategories.NotYetValid => "The presented certificate is not yet valid.",
        SslFailureCategories.HostnameMismatch =>
            "The presented certificate does not cover the requested host.",
        SslFailureCategories.Untrusted => "The presented certificate chain is not trusted.",
        SslFailureCategories.HandshakeFailed =>
            "The TLS handshake failed before a certificate could be inspected.",
        _ => "The certificate could not be inspected."
    };

    private static int ToBoundedMilliseconds(TimeSpan duration) =>
        (int)Math.Clamp(Math.Ceiling(duration.TotalMilliseconds), 0, int.MaxValue);

    private static string Bounded(string value) => value.Length <= 200 ? value : value[..200];
}

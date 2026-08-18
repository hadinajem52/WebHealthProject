using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

public static class SslMonitorIdentity
{
    public const string MonitorType = "SslCertificate";
    public const string DefaultDiscriminator = "default";

    /// <summary>
    /// The expiry rule is keyed by certificate rather than by endpoint, so its issue key is the
    /// only one on this monitor that carries a real discriminator.
    /// </summary>
    public const string ExpiryRuleKey = "Ssl.ExpiringSoon";

    public static string CreateIssueKey(string ruleKey, string discriminator = DefaultDiscriminator) =>
        $"v1|{MonitorType}|{ruleKey}|{discriminator}";

    /// <summary>
    /// BR-C05: one active expiry incident per endpoint <em>and current fingerprint</em>. Putting
    /// the fingerprint in the issue key means the existing unique index on active
    /// (endpoint monitor, issue key) incidents already enforces the rule — repeated daily checks
    /// of the same certificate reuse one incident, and no new constraint is needed.
    /// </summary>
    public static string CreateExpiryIssueKey(string sha256Fingerprint) =>
        CreateIssueKey(ExpiryRuleKey, sha256Fingerprint);

    /// <summary>
    /// BR-C06: true when the issue key belongs to the expiry rule for some <em>other</em>
    /// certificate than the one just observed. A renewed certificate has a new fingerprint and
    /// therefore a new issue key, which leaves the previous key with nothing left to observe;
    /// this is how the incident it opened is recognised as superseded.
    /// </summary>
    public static bool IsSupersededExpiryIssueKey(string issueKey, string currentSha256Fingerprint) =>
        issueKey.StartsWith(ExpiryIssueKeyPrefix, StringComparison.Ordinal)
        && !string.Equals(issueKey, CreateExpiryIssueKey(currentSha256Fingerprint), StringComparison.Ordinal);

    private static string ExpiryIssueKeyPrefix => $"v1|{MonitorType}|{ExpiryRuleKey}|";
}

/// <summary>
/// Certificate-specific result categories. Transport-level problems reuse the shared
/// categories, because a DNS or connection failure means the same thing whichever monitor hit
/// it.
/// </summary>
public static class SslFailureCategories
{
    public const string Expired = "SslExpired";
    public const string NotYetValid = "SslNotYetValid";
    public const string HostnameMismatch = "SslHostnameMismatch";
    public const string Untrusted = "SslUntrusted";
    public const string HandshakeFailed = "SslHandshakeFailed";

    /// <summary>
    /// BR-C04: a currently valid certificate inside one of the expiry bands. It is a distinct
    /// category from <see cref="Expired" />, because the certificate still works today.
    /// </summary>
    public const string ExpiringSoon = "SslExpiringSoon";

    public static readonly string[] All =
        [Expired, NotYetValid, HostnameMismatch, Untrusted, HandshakeFailed, ExpiringSoon];
}

public sealed record NormalizeSslResult(
    SslCertificateProbeResult Probe,
    DateTimeOffset MeasuredAt,
    CertificateExpiryThresholds? ExpiryThresholds = null)
{
    public CertificateExpiryThresholds EffectiveExpiryThresholds =>
        ExpiryThresholds ?? CertificateExpiryThresholds.Default;
}

public static class SslResultNormalizer
{
    public const string MonitorSource = "WebHealthSslProbeV1";

    public static NormalizedCheckResult Normalize(NormalizeSslResult input)
    {
        var findings = Evaluate(input).ToArray();
        var category = SelectFailureCategory(input.Probe, findings);
        return new(
            SelectOutcome(input.Probe, findings),
            category,
            null,
            ToBoundedMilliseconds(input.Probe.Duration),
            null,
            null,
            null,
            MonitorSource,
            input.MeasuredAt,
            Diagnostic(category, findings),
            [],
            findings);
    }

    private static IEnumerable<NormalizedFinding> Evaluate(NormalizeSslResult input)
    {
        if (input.Probe.Failure == SslProbeFailureKind.Cancelled)
        {
            // A cancelled probe observed nothing. Reporting a finding would let a shutdown
            // open an incident.
            yield break;
        }

        var validation = SelectValidationCategory(input.Probe);
        if (validation is not null)
        {
            yield return ValidationFinding(validation, input.Probe.Certificate);
            yield break;
        }

        // BR-C04. Only a certificate that is valid today gets an expiry band: an expired or
        // untrusted one already has its own critical finding, and stacking a second one on it
        // would double-count the same certificate as two issues.
        var expiry = EvaluateExpiry(input);
        if (expiry is not null)
        {
            yield return expiry;
        }
    }

    /// <summary>
    /// BR-C04, keyed by fingerprint for BR-C05. Days remaining are counted from the result's
    /// measurement instant, the same instant every other rule on this result is judged at, so
    /// one result never mixes two clocks.
    /// </summary>
    private static NormalizedFinding? EvaluateExpiry(NormalizeSslResult input)
    {
        if (input.Probe.Certificate is not { } certificate)
        {
            return null;
        }

        var thresholds = input.EffectiveExpiryThresholds;
        var daysRemaining = CertificateExpiry.DaysRemaining(certificate.NotAfter, input.MeasuredAt);
        var severity = CertificateExpiry.SelectSeverity(daysRemaining, thresholds);
        return severity == CertificateExpirySeverity.None
            ? null
            : new NormalizedFinding(
                SslFailureCategories.ExpiringSoon,
                SslMonitorIdentity.ExpiryRuleKey,
                ToFindingSeverity(severity),
                Bounded($"{daysRemaining} days remaining; expires {certificate.NotAfter:yyyy-MM-dd}"),
                $"More than {thresholds.WarningDays} days remaining",
                SslMonitorIdentity.CreateExpiryIssueKey(certificate.Sha256Fingerprint));
    }

    private static string ToFindingSeverity(CertificateExpirySeverity severity) => severity switch
    {
        CertificateExpirySeverity.Critical => FindingSeverities.Critical,
        CertificateExpirySeverity.High => FindingSeverities.High,
        _ => FindingSeverities.Warning
    };

    /// <summary>
    /// A certificate that was observed but rejected is still a completed observation, so the
    /// category comes from its validation state. Only a probe that produced no certificate at
    /// all falls back to a transport-level category.
    /// </summary>
    private static string? SelectFailureCategory(
        SslCertificateProbeResult probe,
        IReadOnlyList<NormalizedFinding> findings) =>
        SelectValidationCategory(probe) ?? findings.FirstOrDefault()?.FailureCategory;

    private static string? SelectValidationCategory(SslCertificateProbeResult probe)
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

    private static string SelectOutcome(
        SslCertificateProbeResult probe,
        IReadOnlyList<NormalizedFinding> findings)
    {
        if (probe.Failure == SslProbeFailureKind.Cancelled)
        {
            return HttpResultOutcomes.Cancelled;
        }

        return findings.Any(finding =>
            FindingSeverities.ToOutcome(finding.Severity) == HttpResultOutcomes.Critical)
                ? HttpResultOutcomes.Critical
                : findings.Count > 0 ? HttpResultOutcomes.Warning : HttpResultOutcomes.Healthy;
    }

    private static NormalizedFinding ValidationFinding(string category, TlsCertificateObservation? certificate) =>
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

    private static string? Diagnostic(string? category, IReadOnlyList<NormalizedFinding> findings) =>
        category == SslFailureCategories.ExpiringSoon
            ? Bounded($"The certificate expires soon: {findings[0].ObservedValue}.")
            : Diagnostic(category);

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

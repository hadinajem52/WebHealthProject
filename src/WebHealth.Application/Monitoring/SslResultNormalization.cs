using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

public static class SslMonitorIdentity
{
    public const string MonitorType = "SslCertificate";
    public const string DefaultDiscriminator = "default";

    public const string ExpiryRuleKey = "Ssl.Expiry";

    public static string CreateIssueKey(string ruleKey, string discriminator = DefaultDiscriminator) =>
        $"v1|{MonitorType}|{ruleKey}|{discriminator}";

    public static string CreateExpiryIssueKey(string sha256Fingerprint) =>
        CreateIssueKey(ExpiryRuleKey, sha256Fingerprint);

    public static bool IsSupersededExpiryIssueKey(string issueKey, string currentSha256Fingerprint) =>
        issueKey.StartsWith(ExpiryIssueKeyPrefix, StringComparison.Ordinal)
        && !string.Equals(issueKey, CreateExpiryIssueKey(currentSha256Fingerprint), StringComparison.Ordinal);

    private static string ExpiryIssueKeyPrefix => $"v1|{MonitorType}|{ExpiryRuleKey}|";
}

public static class SslFailureCategories
{
    public const string Expired = "SslExpired";
    public const string NotYetValid = "SslNotYetValid";
    public const string HostnameMismatch = "SslHostnameMismatch";
    public const string Untrusted = "SslUntrusted";
    public const string HandshakeFailed = "SslHandshakeFailed";

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
            yield break;
        }

        var validation = SelectValidationCategory(input.Probe);

        if (validation is null || validation == SslFailureCategories.Expired)
        {
            var expiry = EvaluateExpiry(input, validation);
            if (expiry is not null)
            {
                yield return expiry;
            }

            yield break;
        }

        yield return ValidationFinding(validation, input.Probe.Certificate);
    }

    private static NormalizedFinding? EvaluateExpiry(NormalizeSslResult input, string? validationCategory)
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
                validationCategory ?? SslFailureCategories.ExpiringSoon,
                SslMonitorIdentity.ExpiryRuleKey,
                ToFindingSeverity(severity),
                Bounded(validationCategory is null
                    ? $"{daysRemaining} days remaining; expires {certificate.NotAfter:yyyy-MM-dd}"
                    : $"Expired {-daysRemaining} days ago on {certificate.NotAfter:yyyy-MM-dd}"),
                $"More than {thresholds.WarningDays} days remaining",
                SslMonitorIdentity.CreateExpiryIssueKey(certificate.Sha256Fingerprint));
    }

    private static string ToFindingSeverity(CertificateExpirySeverity severity) => severity switch
    {
        CertificateExpirySeverity.Critical => FindingSeverities.Critical,
        CertificateExpirySeverity.High => FindingSeverities.High,
        _ => FindingSeverities.Warning
    };

    private static string? SelectFailureCategory(
        SslCertificateProbeResult probe,
        IReadOnlyList<NormalizedFinding> findings) =>
        findings.FirstOrDefault()?.FailureCategory ?? SelectValidationCategory(probe);

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
            SslProbeFailureKind.DestinationRejected =>
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

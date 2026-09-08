using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

public sealed record ResolvedSslPolicy(
    int IntervalSeconds = 86400,
    int TimeoutSeconds = 15,
    int FailureConfirmationCount = 1,
    int RecoveryConfirmationCount = 1,
    int WarningExpiryDays = 30,
    int HighExpiryDays = 15,
    int CriticalExpiryDays = 7)
{
    public static ResolvedSslPolicy Default { get; } = new();

    public CertificateExpiryThresholds ExpiryThresholds => new(WarningExpiryDays, HighExpiryDays, CriticalExpiryDays);

    public void Validate()
    {
        if (IntervalSeconds <= 0 || TimeoutSeconds is < 1 or > 120
            || FailureConfirmationCount is < 1 or > 10 || RecoveryConfirmationCount is < 1 or > 10)
        {
            throw new ArgumentException("SSL cadence, timeout, and confirmation counts must be within supported bounds.");
        }
        CertificateExpiry.SelectSeverity(0, ExpiryThresholds);
    }

    public string LegacyFingerprint(string normalizedUrl, bool isProduction) => HttpPolicyFingerprint.Create(new(
        normalizedUrl, SslMonitorIdentity.MonitorType, isProduction, IntervalSeconds, TimeoutSeconds,
        FailureConfirmationCount, RecoveryConfirmationCount, null, null, [], null, "OrdinalIgnoreCase",
        FindingSeverities.Warning, SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes, SafeHttpTransportDefaults.MaxRedirects));

    public bool MatchesFingerprint(string fingerprint, string normalizedUrl, bool isProduction) =>
        string.Equals(fingerprint, Fingerprint(normalizedUrl, isProduction), StringComparison.Ordinal)
        || (ExpiryThresholds == CertificateExpiryThresholds.Default
            && string.Equals(fingerprint, LegacyFingerprint(normalizedUrl, isProduction), StringComparison.Ordinal));

    public string Fingerprint(string normalizedUrl, bool isProduction)
    {
        Validate();
        var canonical = new StringBuilder("ssl-v1|");
        foreach (var value in new[] { normalizedUrl, SslMonitorIdentity.MonitorType, isProduction ? "1" : "0",
            IntervalSeconds.ToString(CultureInfo.InvariantCulture), TimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            FailureConfirmationCount.ToString(CultureInfo.InvariantCulture), RecoveryConfirmationCount.ToString(CultureInfo.InvariantCulture),
            WarningExpiryDays.ToString(CultureInfo.InvariantCulture), HighExpiryDays.ToString(CultureInfo.InvariantCulture),
            CriticalExpiryDays.ToString(CultureInfo.InvariantCulture) })
        {
            canonical.Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('|');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

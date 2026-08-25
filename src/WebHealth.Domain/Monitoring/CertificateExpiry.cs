namespace WebHealth.Domain.Monitoring;

public enum CertificateExpirySeverity
{
    None,
    Warning,
    High,
    Critical
}

public sealed record CertificateExpiryThresholds(int WarningDays, int HighDays, int CriticalDays)
{
    public static CertificateExpiryThresholds Default { get; } = new(30, 15, 7);
}

public static class CertificateExpiry
{
    public static int DaysRemaining(DateTimeOffset notAfter, DateTimeOffset observedAt) =>
        (int)Math.Clamp(
            Math.Truncate((notAfter - observedAt).TotalDays),
            int.MinValue,
            int.MaxValue);

    public static CertificateExpirySeverity SelectSeverity(
        int daysRemaining,
        CertificateExpiryThresholds thresholds)
    {
        Validate(thresholds);

        if (daysRemaining <= thresholds.CriticalDays)
        {
            return CertificateExpirySeverity.Critical;
        }

        if (daysRemaining <= thresholds.HighDays)
        {
            return CertificateExpirySeverity.High;
        }

        return daysRemaining <= thresholds.WarningDays
            ? CertificateExpirySeverity.Warning
            : CertificateExpirySeverity.None;
    }

    private static void Validate(CertificateExpiryThresholds thresholds)
    {
        if (thresholds.CriticalDays < 0
            || thresholds.CriticalDays > thresholds.HighDays
            || thresholds.HighDays > thresholds.WarningDays)
        {
            throw new ArgumentException(
                "Certificate expiry thresholds must be non-negative and ordered critical <= high <= warning.",
                nameof(thresholds));
        }
    }
}

namespace WebHealth.Domain.Monitoring;

/// <summary>
/// Expiry urgency band for an otherwise valid certificate (BR-C04). <c>None</c> means the
/// certificate is far enough from expiry to raise nothing at all.
/// </summary>
public enum CertificateExpirySeverity
{
    None,
    Warning,
    High,
    Critical
}

/// <summary>
/// Day counts at which an expiry band begins, largest window first. The defaults are the
/// BR-C04 values; the record exists so tests can pin the boundary behaviour independently of
/// the numbers.
/// </summary>
public sealed record CertificateExpiryThresholds(int WarningDays, int HighDays, int CriticalDays)
{
    public static CertificateExpiryThresholds Default { get; } = new(30, 15, 7);
}

public static class CertificateExpiry
{
    /// <summary>
    /// Whole days left in the certificate's validity window, counted from the observation
    /// instant and truncated toward zero: a certificate expiring in 29 hours has one day left,
    /// not two. An already-expired certificate reports a negative count rather than clamping to
    /// zero, so "expires today" and "expired a week ago" stay distinguishable.
    /// </summary>
    public static int DaysRemaining(DateTimeOffset notAfter, DateTimeOffset observedAt) =>
        (int)Math.Clamp(
            Math.Truncate((notAfter - observedAt).TotalDays),
            int.MinValue,
            int.MaxValue);

    /// <summary>
    /// Selects the BR-C04 expiry band for a days-remaining count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every boundary is <em>inclusive on the unhealthy side</em>: exactly 30 days remaining is
    /// already a warning, exactly 15 is already high, and exactly 7 is already critical, while
    /// 31, 16 and 8 sit one band lower. The comparison direction is written down here rather
    /// than inferred from the operators, because AC-06 tests both sides of all three
    /// boundaries and an off-by-one would otherwise look like a plausible reading of the rule.
    /// </para>
    /// <para>
    /// An expired certificate reports a negative count and therefore lands in the critical
    /// band by the same comparison — no separate case, and no way for a very negative count to
    /// fall out of the bands.
    /// </para>
    /// </remarks>
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

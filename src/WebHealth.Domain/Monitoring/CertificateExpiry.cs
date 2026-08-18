namespace WebHealth.Domain.Monitoring;

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
}

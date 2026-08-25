namespace WebHealth.Domain.Monitoring;

public enum PerformanceSeverity
{
    None,
    Warning,
    Critical
}

public sealed record ResponseTimeThresholds(int WarningMs, int CriticalMs)
{
    public static ResponseTimeThresholds Default { get; } = new(1_500, 3_000);
}

public static class PerformanceEvaluation
{
    public const long DefaultPageSizeWarningBytes = 2L * 1024 * 1024;

    public static PerformanceSeverity SelectResponseTimeSeverity(
        int totalDurationMs,
        ResponseTimeThresholds thresholds)
    {
        Validate(thresholds);

        if (totalDurationMs >= thresholds.CriticalMs)
        {
            return PerformanceSeverity.Critical;
        }

        return totalDurationMs >= thresholds.WarningMs
            ? PerformanceSeverity.Warning
            : PerformanceSeverity.None;
    }

    public static PerformanceSeverity SelectPageSizeSeverity(long measuredBytes, long warningBytes)
    {
        if (warningBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(warningBytes),
                "The page-size warning threshold must be positive.");
        }

        return measuredBytes >= warningBytes ? PerformanceSeverity.Warning : PerformanceSeverity.None;
    }

    private static void Validate(ResponseTimeThresholds thresholds)
    {
        if (thresholds.WarningMs <= 0 || thresholds.CriticalMs < thresholds.WarningMs)
        {
            throw new ArgumentException(
                "Response-time thresholds must be positive and ordered warning <= critical.",
                nameof(thresholds));
        }
    }
}

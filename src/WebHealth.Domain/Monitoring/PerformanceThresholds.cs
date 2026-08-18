namespace WebHealth.Domain.Monitoring;

/// <summary>
/// Threshold band for one performance measurement (BR-P02, BR-P04). <c>None</c> means the
/// measurement is inside budget and raises nothing.
/// </summary>
public enum PerformanceSeverity
{
    None,
    Warning,
    Critical
}

/// <summary>
/// Total response-time budget in milliseconds (BR-P02). The defaults are the specified 1,500
/// and 3,000 ms; an endpoint may override them, in which case the override is snapshotted with
/// the check so a stored result keeps the thresholds it was judged against.
/// </summary>
public sealed record ResponseTimeThresholds(int WarningMs, int CriticalMs)
{
    public static ResponseTimeThresholds Default { get; } = new(1_500, 3_000);
}

public static class PerformanceEvaluation
{
    /// <summary>
    /// The default page-size warning threshold (BR-P04): 2 MB of page content.
    /// </summary>
    public const long DefaultPageSizeWarningBytes = 2L * 1024 * 1024;

    /// <summary>
    /// Selects the response-time band for a measured total duration.
    /// </summary>
    /// <remarks>
    /// Thresholds are <em>inclusive on the unhealthy side</em>, the same direction as the
    /// certificate expiry bands: a threshold of 1,500 ms states the response should stay under
    /// 1,500 ms, so exactly 1,500 ms has already missed it. One written direction across both
    /// rule families means a boundary test never has to guess which way a given rule leans.
    /// </remarks>
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

    /// <summary>
    /// Page size is a warning-only rule (BR-P04): a large page is a quality problem, never a
    /// reason to call the endpoint down. The boundary is inclusive on the unhealthy side, so a
    /// page of exactly the threshold already warns.
    /// </summary>
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

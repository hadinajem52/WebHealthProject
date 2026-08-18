using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Registry;

public sealed record ResponseThresholdDecision(ResponseTimeThresholds Thresholds, string? Error);

/// <summary>
/// BR-P02: an endpoint may override the 1,500 / 3,000 ms response-time budget. Submitting
/// neither value means "use the documented defaults"; submitting one without the other is
/// rejected rather than silently half-applied, because a warning threshold above an unchanged
/// critical one would produce a band that can never be reached.
/// </summary>
public static class ResponseThresholdOverride
{
    public const int MinimumMs = 1;
    public const int MaximumMs = 300_000;

    public static ResponseThresholdDecision Decide(int? warningMs, int? criticalMs)
    {
        if (warningMs is null && criticalMs is null)
        {
            return new(ResponseTimeThresholds.Default, null);
        }

        if (warningMs is null || criticalMs is null)
        {
            return new(
                ResponseTimeThresholds.Default,
                "Set both the warning and critical response-time thresholds, or neither.");
        }

        if (warningMs is < MinimumMs or > MaximumMs || criticalMs is < MinimumMs or > MaximumMs)
        {
            return new(
                ResponseTimeThresholds.Default,
                $"Response-time thresholds must be between {MinimumMs} and {MaximumMs} ms.");
        }

        return criticalMs < warningMs
            ? new(
                ResponseTimeThresholds.Default,
                "The critical response-time threshold must be at or above the warning threshold.")
            : new(new ResponseTimeThresholds(warningMs.Value, criticalMs.Value), null);
    }

    /// <summary>
    /// Whether stored thresholds differ from the documented defaults, so the endpoint page can
    /// say "endpoint override" rather than implying the operator chose the default explicitly.
    /// </summary>
    public static bool IsOverride(int? warningMs, int? criticalMs) =>
        (warningMs ?? ResponseTimeThresholds.Default.WarningMs) != ResponseTimeThresholds.Default.WarningMs
        || (criticalMs ?? ResponseTimeThresholds.Default.CriticalMs) != ResponseTimeThresholds.Default.CriticalMs;
}

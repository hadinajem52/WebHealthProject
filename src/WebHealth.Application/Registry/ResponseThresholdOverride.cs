using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Registry;

public sealed record ResponseThresholdDecision(ResponseTimeThresholds Thresholds, string? Error);

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

    public static bool IsOverride(int? warningMs, int? criticalMs) =>
        (warningMs ?? ResponseTimeThresholds.Default.WarningMs) != ResponseTimeThresholds.Default.WarningMs
        || (criticalMs ?? ResponseTimeThresholds.Default.CriticalMs) != ResponseTimeThresholds.Default.CriticalMs;
}

using WebHealth.Domain.Health;

namespace WebHealth.Application.Monitoring;

public sealed record MonitorOperationalState(
    string ConfirmedHealth,
    string State,
    string BaseState,
    bool IsDelayed,
    bool IsStale,
    DateTimeOffset? LastScheduledCompletionAt,
    DateTimeOffset? StaleAfter)
{
    public static MonitorOperationalState Evaluate(
        string? confirmedHealth,
        bool lifecycleEligible,
        bool schedulingEnabled,
        bool monitorEnabled,
        int intervalSeconds,
        DateTimeOffset nextDueAt,
        DateTimeOffset? lastScheduledCompletionAt,
        DateTimeOffset now,
        TimeSpan dispatchDelayGrace)
    {
        if (intervalSeconds <= 0 || dispatchDelayGrace < TimeSpan.FromMinutes(2)
            || dispatchDelayGrace > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Cadence and dispatch delay grace must be within supported bounds.");
        }

        var health = confirmedHealth is null or EndpointHealthStatuses.Disabled
            ? EndpointHealthStatuses.Unknown : confirmedHealth;
        var baseState = !lifecycleEligible ? "Disabled"
            : !schedulingEnabled ? "ManualOnly"
            : !monitorEnabled ? "Paused" : "Active";
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var freshnessGrace = TimeSpan.FromSeconds(Math.Max(600, intervalSeconds / 4d));
        var staleAfter = lastScheduledCompletionAt + interval + freshnessGrace;
        var delayed = baseState == "Active" && now - nextDueAt > dispatchDelayGrace;
        var stale = staleAfter is { } cutoff && now > cutoff;
        var state = baseState != "Active" ? baseState
            : delayed ? "Delayed"
            : lastScheduledCompletionAt is null ? "NeverChecked"
            : stale ? "Stale" : "Active";
        return new(health, state, baseState, delayed, stale, lastScheduledCompletionAt, staleAfter);
    }
}

public sealed record MonitorOperationalStatus(string MonitorType, MonitorOperationalState Status);

using Microsoft.EntityFrameworkCore;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.PageAudits;

internal static class PageAuditConfiguration
{
    public static string? Validate(
        bool enabled,
        bool schedulingEnabled,
        int intervalHours,
        string normalizedUrl)
    {
        if (!PageAuditCadence.IsSupported(intervalHours))
        {
            return "The PageSpeed audit interval must be between "
                + $"{PageAuditCadence.MinimumIntervalHours} hours and "
                + $"{PageAuditCadence.MaximumIntervalHours / 24} days.";
        }

        if (schedulingEnabled && !enabled)
        {
            return "Enable PageSpeed auditing before scheduling it.";
        }

        if (!enabled)
        {
            return null;
        }

        var eligibility = PageAuditEligibility.Evaluate(normalizedUrl);
        return eligibility.IsEligible ? null : Describe(eligibility.Reason);
    }

    public static async Task<bool> ApplyAsync(
        ApplicationDbContext dbContext,
        Guid endpointId,
        bool enabled,
        bool schedulingEnabled,
        int intervalHours,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var targets = await dbContext.PageAuditTargets
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights)
            .ToArrayAsync(cancellationToken);

        var changed = false;
        if (enabled && !await dbContext.PageAuditIncidentPolicies
                .AnyAsync(policy => policy.EndpointId == endpointId, cancellationToken))
        {
            dbContext.PageAuditIncidentPolicies.Add(
                PageAuditIncidentPolicyDefaults.Create(endpointId, now));
            changed = true;
        }

        foreach (var category in PageAuditCategories.All)
        {
            foreach (var strategy in PageAuditStrategies.All)
            {
                changed |= ApplyProfile(
                    dbContext,
                    endpointId,
                    category,
                    strategy,
                    targets.SingleOrDefault(candidate => candidate.Category == category
                        && candidate.Strategy == strategy),
                    enabled,
                    schedulingEnabled,
                    intervalHours * 3600,
                    now);
            }
        }

        return changed;
    }

    private static bool ApplyProfile(
        ApplicationDbContext dbContext,
        Guid endpointId,
        string category,
        string strategy,
        PageAuditTarget? target,
        bool enabled,
        bool schedulingEnabled,
        int intervalSeconds,
        DateTimeOffset now)
    {
        if (target is null)
        {
            if (!enabled)
            {
                return false;
            }

            dbContext.PageAuditTargets.Add(new PageAuditTarget
            {
                Id = Guid.NewGuid(),
                EndpointId = endpointId,
                Provider = PageAuditProviders.PageSpeedInsights,
                Category = category,
                Strategy = strategy,
                IsEnabled = true,
                SchedulingEnabled = schedulingEnabled,
                IntervalSeconds = intervalSeconds,
                ScheduleAnchor = now,

                NextDueAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            });
            return true;
        }

        var changed = target.IsEnabled != enabled
            || target.SchedulingEnabled != schedulingEnabled
            || target.IntervalSeconds != intervalSeconds;
        if (!changed)
        {
            return false;
        }

        if (target.IntervalSeconds != intervalSeconds)
        {
            target.IntervalSeconds = intervalSeconds;
            target.ScheduleAnchor = now;
            target.NextDueAt = MonitorCadence.GetFirstSlotAfter(now, intervalSeconds, now);
        }

        if (!target.SchedulingEnabled && schedulingEnabled)
        {
            target.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                target.ScheduleAnchor, target.IntervalSeconds, now);
        }

        target.IsEnabled = enabled;
        target.SchedulingEnabled = schedulingEnabled;
        target.UpdatedAt = now;
        target.Version++;
        return true;
    }

    public static async Task<PageAuditConfigurationState> ReadAsync(
        ApplicationDbContext dbContext,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.PageAuditTargets.AsNoTracking()
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights)
            .OrderBy(candidate => candidate.Category == PageAuditCategories.Performance ? 0 : 1)
            .ThenBy(candidate => candidate.Strategy)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new PageAuditConfigurationState(
                candidate.IsEnabled,
                candidate.SchedulingEnabled,
                candidate.IntervalSeconds / 3600))
            .FirstOrDefaultAsync(cancellationToken);
        return target ?? PageAuditConfigurationState.Default;
    }

    private static string Describe(string? reason) => reason switch
    {
        PageAuditIneligibilityReasons.HostNotPublic or PageAuditIneligibilityReasons.AddressNotPublic =>
            "PageSpeed auditing needs a URL Google can reach from the public internet. This host "
            + "is internal, so it cannot be audited.",
        PageAuditIneligibilityReasons.UrlCarriesCredentials =>
            "This URL carries credentials, which must not be sent to a third party.",
        PageAuditIneligibilityReasons.SchemeNotSupported =>
            "Only http and https pages can be audited.",
        _ => "This endpoint URL cannot be audited."
    };
}

public sealed record PageAuditConfigurationState(
    bool Enabled,
    bool SchedulingEnabled,
    int IntervalHours)
{
    public static PageAuditConfigurationState Default { get; } =
        new(false, false, PageAuditCadence.DefaultIntervalHours);
}

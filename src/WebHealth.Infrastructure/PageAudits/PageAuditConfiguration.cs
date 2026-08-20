using Microsoft.EntityFrameworkCore;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// Creates and updates an endpoint's page-audit target inside the endpoint's own transaction.
/// </summary>
/// <remarks>
/// It lives beside the endpoint mutation rather than behind a separate call because the two must
/// commit together. A target row enabled by a transaction that then rolled back would schedule
/// audits for a configuration nobody saved.
/// </remarks>
internal static class PageAuditConfiguration
{
    /// <summary>
    /// Why this configuration cannot be saved, or null.
    /// </summary>
    /// <remarks>
    /// Eligibility is checked at configuration time as well as at dispatch. Refusing here is what
    /// lets an operator find out that an internal URL cannot be audited while they are looking at
    /// the form, rather than a day later from a failed run.
    /// </remarks>
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

    /// <summary>
    /// Brings the endpoint's target row into line with the submitted configuration, and returns
    /// whether anything changed so the caller can decide what to record.
    /// </summary>
    /// <remarks>
    /// A disabled target is kept rather than deleted. Deleting it would orphan the run history
    /// that references it, and switching the feature off is not a request to forget every score
    /// it ever produced.
    /// </remarks>
    public static async Task<bool> ApplyAsync(
        ApplicationDbContext dbContext,
        Guid endpointId,
        bool enabled,
        bool schedulingEnabled,
        int intervalHours,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.PageAuditTargets.SingleOrDefaultAsync(
            candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights
                && candidate.Category == PageAuditCategories.Seo
                && candidate.Strategy == PageAuditStrategies.Mobile,
            cancellationToken);

        var intervalSeconds = intervalHours * 3600;
        if (target is null)
        {
            // Nothing to store for an endpoint that never had the feature turned on: an empty row
            // would put every endpoint in the scheduler's table for no reason.
            if (!enabled)
            {
                return false;
            }

            dbContext.PageAuditTargets.Add(new PageAuditTarget
            {
                Id = Guid.NewGuid(),
                EndpointId = endpointId,
                Provider = PageAuditProviders.PageSpeedInsights,
                Category = PageAuditCategories.Seo,
                Strategy = PageAuditStrategies.Mobile,
                IsEnabled = true,
                SchedulingEnabled = schedulingEnabled,
                IntervalSeconds = intervalSeconds,
                ScheduleAnchor = now,

                // Due immediately, so enabling the feature produces a score to look at rather than
                // a promise of one tomorrow.
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

        // A cadence change re-anchors, so the new interval counts from now rather than from an
        // anchor set when the endpoint was created.
        if (target.IntervalSeconds != intervalSeconds)
        {
            target.IntervalSeconds = intervalSeconds;
            target.ScheduleAnchor = now;
            target.NextDueAt = MonitorCadence.GetFirstSlotAfter(now, intervalSeconds, now);
        }

        // Scheduling resumed after a pause runs at the next slot, not immediately: resuming is not
        // a request for a fresh audit, and treating it as one would spend quota on every toggle.
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

    /// <summary>The current configuration for a reader, or the defaults when there is none.</summary>
    public static async Task<PageAuditConfigurationState> ReadAsync(
        ApplicationDbContext dbContext,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.PageAuditTargets.AsNoTracking()
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights
                && candidate.Category == PageAuditCategories.Seo
                && candidate.Strategy == PageAuditStrategies.Mobile)
            .Select(candidate => new PageAuditConfigurationState(
                candidate.IsEnabled,
                candidate.SchedulingEnabled,
                candidate.IntervalSeconds / 3600))
            .SingleOrDefaultAsync(cancellationToken);
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

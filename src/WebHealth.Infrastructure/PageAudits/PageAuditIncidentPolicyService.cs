using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.PageAudits;

internal sealed class PageAuditIncidentPolicyService(
    ApplicationDbContext dbContext,
    IAuditTrailWriter auditTrail,
    TimeProvider timeProvider) : IPageAuditIncidentPolicyService
{
    public async Task<PageAuditIncidentPolicy> GetAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        ToPolicy(await dbContext.PageAuditIncidentPolicies.AsNoTracking()
            .SingleAsync(policy => policy.EndpointId == endpointId, cancellationToken));

    public async Task<PageAuditIncidentPolicyUpdateResult> UpdateAsync(
        Guid endpointId,
        UpdatePageAuditIncidentPolicy command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PageAuditIncidentPolicies
            .SingleAsync(policy => policy.EndpointId == endpointId, cancellationToken);
        var current = ToPolicy(entity);
        var errors = PageAuditIncidentEvaluator.Validate(command);
        if (errors.Count > 0)
        {
            return new(false, false, current, errors);
        }

        if (entity.Version != command.Version)
        {
            return new(false, true, current, ["These settings changed while you were editing them. Review the current values and save again."]);
        }

        var before = ToAudit(current);
        Apply(entity, command, actorUserId, timeProvider.GetUtcNow());
        var updated = ToPolicy(entity);
        try
        {
            await auditTrail.RecordPageAuditIncidentPolicyMutationAsync(
                new(actorUserId, entity.UpdatedAt),
                before,
                ToAudit(updated),
                cancellationToken);
            return new(true, false, updated, []);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var latest = await GetAsync(endpointId, cancellationToken);
            return new(false, true, latest, ["These settings changed while you were editing them. Review the current values and save again."]);
        }
    }

    private static void Apply(
        PageAuditIncidentPolicyEntity entity,
        UpdatePageAuditIncidentPolicy command,
        Guid actorUserId,
        DateTimeOffset now)
    {
        entity.IncidentsEnabled = command.IncidentsEnabled;
        entity.PerformanceScoreEnabled = command.PerformanceScoreEnabled;
        entity.PerformanceMinimumScore = command.PerformanceMinimumScore;
        entity.AccessibilityScoreEnabled = command.AccessibilityScoreEnabled;
        entity.AccessibilityMinimumScore = command.AccessibilityMinimumScore;
        entity.BestPracticesScoreEnabled = command.BestPracticesScoreEnabled;
        entity.BestPracticesMinimumScore = command.BestPracticesMinimumScore;
        entity.SeoScoreEnabled = command.SeoScoreEnabled;
        entity.SeoMinimumScore = command.SeoMinimumScore;
        entity.FirstContentfulPaintEnabled = command.FirstContentfulPaintEnabled;
        entity.FirstContentfulPaintMaximum = command.FirstContentfulPaintMaximum;
        entity.LargestContentfulPaintEnabled = command.LargestContentfulPaintEnabled;
        entity.LargestContentfulPaintMaximum = command.LargestContentfulPaintMaximum;
        entity.TotalBlockingTimeEnabled = command.TotalBlockingTimeEnabled;
        entity.TotalBlockingTimeMaximum = command.TotalBlockingTimeMaximum;
        entity.CumulativeLayoutShiftEnabled = command.CumulativeLayoutShiftEnabled;
        entity.CumulativeLayoutShiftMaximum = command.CumulativeLayoutShiftMaximum;
        entity.SpeedIndexEnabled = command.SpeedIndexEnabled;
        entity.SpeedIndexMaximum = command.SpeedIndexMaximum;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = actorUserId;
        entity.Version++;
    }

    internal static PageAuditIncidentPolicy ToPolicy(PageAuditIncidentPolicyEntity entity) => new(
        entity.EndpointId,
        entity.IncidentsEnabled,
        entity.PerformanceScoreEnabled,
        entity.PerformanceMinimumScore,
        entity.AccessibilityScoreEnabled,
        entity.AccessibilityMinimumScore,
        entity.BestPracticesScoreEnabled,
        entity.BestPracticesMinimumScore,
        entity.SeoScoreEnabled,
        entity.SeoMinimumScore,
        entity.FirstContentfulPaintEnabled,
        entity.FirstContentfulPaintMaximum,
        entity.LargestContentfulPaintEnabled,
        entity.LargestContentfulPaintMaximum,
        entity.TotalBlockingTimeEnabled,
        entity.TotalBlockingTimeMaximum,
        entity.CumulativeLayoutShiftEnabled,
        entity.CumulativeLayoutShiftMaximum,
        entity.SpeedIndexEnabled,
        entity.SpeedIndexMaximum,
        entity.Version);

    private static PageAuditIncidentPolicyAuditSnapshot ToAudit(PageAuditIncidentPolicy policy) => new(
        policy.EndpointId,
        policy.IncidentsEnabled,
        new Dictionary<string, decimal?>(StringComparer.Ordinal)
        {
            [PageAuditCategories.Performance] = policy.PerformanceScoreEnabled ? policy.PerformanceMinimumScore : null,
            [PageAuditCategories.Accessibility] = policy.AccessibilityScoreEnabled ? policy.AccessibilityMinimumScore : null,
            [PageAuditCategories.BestPractices] = policy.BestPracticesScoreEnabled ? policy.BestPracticesMinimumScore : null,
            [PageAuditCategories.Seo] = policy.SeoScoreEnabled ? policy.SeoMinimumScore : null,
            [PageAuditPerformanceMetrics.FirstContentfulPaint] = policy.FirstContentfulPaintEnabled ? policy.FirstContentfulPaintMaximum : null,
            [PageAuditPerformanceMetrics.LargestContentfulPaint] = policy.LargestContentfulPaintEnabled ? policy.LargestContentfulPaintMaximum : null,
            [PageAuditPerformanceMetrics.TotalBlockingTime] = policy.TotalBlockingTimeEnabled ? policy.TotalBlockingTimeMaximum : null,
            [PageAuditPerformanceMetrics.CumulativeLayoutShift] = policy.CumulativeLayoutShiftEnabled ? policy.CumulativeLayoutShiftMaximum : null,
            [PageAuditPerformanceMetrics.SpeedIndex] = policy.SpeedIndexEnabled ? policy.SpeedIndexMaximum : null
        },
        policy.Version);
}

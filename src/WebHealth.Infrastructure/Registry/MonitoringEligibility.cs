using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal static class MonitoringEligibility
{
    public static IQueryable<Endpoint> Apply(IQueryable<Endpoint> endpoints, DateTimeOffset now) =>
        endpoints.Where(endpoint =>
            endpoint.DeletedAt == null
            && endpoint.IsEnabled
            && endpoint.Environment.DeletedAt == null
            && endpoint.Environment.IsActive
            && endpoint.Environment.Website.DeletedAt == null
            && endpoint.Environment.Website.IsEnabled
            && endpoint.Monitors.Any(monitor => monitor.DeletedAt == null && monitor.IsEnabled)
            && endpoint.TargetAuthorizations.Any(evidence =>
                evidence.RevokedAt == null
                && evidence.EffectiveFrom <= now
                && (evidence.ExpiresAt == null || evidence.ExpiresAt > now)
                && evidence.NormalizedHost == endpoint.NormalizedHost
                && evidence.Port == endpoint.EffectivePort));
}

internal sealed class MonitoringEligibilityService(ApplicationDbContext dbContext) : IMonitoringEligibilityService
{
    public Task<bool> IsEndpointEligibleAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        MonitoringEligibility.Apply(dbContext.Endpoints.AsNoTracking(), DateTimeOffset.UtcNow)
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);
}

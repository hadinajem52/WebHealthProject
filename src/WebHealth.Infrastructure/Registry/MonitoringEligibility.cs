using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal static class MonitoringEligibility
{
    /// <summary>
    /// Endpoints a check may run against at all: registered, enabled up the whole
    /// ownership chain, and covered by current target-authorization evidence.
    /// A paused monitor still satisfies this, so manual runs survive a pause.
    /// </summary>
    public static IQueryable<Endpoint> ApplyTestable(IQueryable<Endpoint> endpoints, DateTimeOffset now) =>
        endpoints.Where(endpoint =>
            endpoint.DeletedAt == null
            && endpoint.IsEnabled
            && endpoint.Environment.DeletedAt == null
            && endpoint.Environment.IsActive
            && endpoint.Environment.Website.DeletedAt == null
            && endpoint.Environment.Website.IsEnabled
            && endpoint.Environment.Website.Client.DeletedAt == null
            && endpoint.Environment.Website.Client.IsActive
            && endpoint.Monitors.Any(monitor => monitor.DeletedAt == null)
            && endpoint.TargetAuthorizations.Any(evidence =>
                evidence.RevokedAt == null
                && evidence.EffectiveFrom <= now
                && (evidence.ExpiresAt == null || evidence.ExpiresAt > now)
                && evidence.NormalizedHost == endpoint.NormalizedHost
                && evidence.Port == endpoint.EffectivePort));

    /// <summary>
    /// Endpoints the scheduler may dispatch: testable, and with an active monitor
    /// cadence. Pausing a monitor removes an endpoint from this set only.
    /// </summary>
    public static IQueryable<Endpoint> Apply(IQueryable<Endpoint> endpoints, DateTimeOffset now) =>
        ApplyTestable(endpoints, now)
            .Where(endpoint => endpoint.Monitors.Any(monitor =>
                monitor.DeletedAt == null && monitor.SchedulingEnabled && monitor.IsEnabled));
}

internal sealed class MonitoringEligibilityService(ApplicationDbContext dbContext) : IMonitoringEligibilityService
{
    public Task<bool> IsEndpointEligibleAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        MonitoringEligibility.Apply(dbContext.Endpoints.AsNoTracking(), DateTimeOffset.UtcNow)
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);

    public Task<bool> IsEndpointTestableAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        MonitoringEligibility.ApplyTestable(dbContext.Endpoints.AsNoTracking(), DateTimeOffset.UtcNow)
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);
}

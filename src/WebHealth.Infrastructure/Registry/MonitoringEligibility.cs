using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal static class MonitoringEligibility
{
    public static IQueryable<Endpoint> ApplyTestable(IQueryable<Endpoint> endpoints, DateTimeOffset now) =>
        endpoints.Where(endpoint =>
            endpoint.DeletedAt == null
            && endpoint.IsEnabled
            && endpoint.Environment.DeletedAt == null
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

    public static IQueryable<EndpointTestReadiness> ProjectTestReadiness(
        IQueryable<Endpoint> endpoints,
        DateTimeOffset now) =>
        endpoints.Select(endpoint => new EndpointTestReadiness(
            endpoint.IsEnabled,
            endpoint.Environment.DeletedAt == null,
            endpoint.Environment.Website.DeletedAt == null && endpoint.Environment.Website.IsEnabled,
            endpoint.Environment.Website.Client.DeletedAt == null && endpoint.Environment.Website.Client.IsActive,
            endpoint.Monitors.Any(monitor => monitor.DeletedAt == null),
            endpoint.TargetAuthorizations.Any(evidence =>
                evidence.RevokedAt == null
                && evidence.EffectiveFrom <= now
                && (evidence.ExpiresAt == null || evidence.ExpiresAt > now)
                && evidence.NormalizedHost == endpoint.NormalizedHost
                && evidence.Port == endpoint.EffectivePort)));

    public static IQueryable<Endpoint> Apply(IQueryable<Endpoint> endpoints, DateTimeOffset now) =>
        ApplyTestable(endpoints, now)
            .Where(endpoint => endpoint.Monitors.Any(monitor =>
                monitor.DeletedAt == null && monitor.SchedulingEnabled && monitor.IsEnabled));
}

internal sealed record EndpointTestReadiness(
    bool EndpointEnabled,
    bool EnvironmentAvailable,
    bool WebsiteEnabled,
    bool ClientActive,
    bool HasMonitor,
    bool HasTargetAuthorization)
{
    public EndpointTestBlock Block => this switch
    {
        { EndpointEnabled: false } => EndpointTestBlock.EndpointDisabled,
        { EnvironmentAvailable: false } => EndpointTestBlock.EnvironmentArchived,
        { WebsiteEnabled: false } => EndpointTestBlock.WebsiteDisabled,
        { ClientActive: false } => EndpointTestBlock.ClientInactive,
        { HasMonitor: false } => EndpointTestBlock.NoMonitor,
        { HasTargetAuthorization: false } => EndpointTestBlock.NoTargetAuthorization,
        _ => EndpointTestBlock.None
    };
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

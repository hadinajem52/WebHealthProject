using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal static class MonitoringEligibility
{
    public static IQueryable<Endpoint> ApplyTestable(IQueryable<Endpoint> endpoints) =>
        endpoints.Where(endpoint =>
            endpoint.DeletedAt == null
            && endpoint.IsEnabled
            && endpoint.Environment.DeletedAt == null
            && endpoint.Environment.Website.DeletedAt == null
            && endpoint.Environment.Website.IsEnabled
            && endpoint.Environment.Website.Client.DeletedAt == null
            && endpoint.Environment.Website.Client.IsActive
            && endpoint.Monitors.Any(monitor => monitor.DeletedAt == null));

    public static IQueryable<EndpointTestReadiness> ProjectTestReadiness(IQueryable<Endpoint> endpoints) =>
        endpoints.Select(endpoint => new EndpointTestReadiness(
            endpoint.IsEnabled,
            endpoint.Environment.DeletedAt == null,
            endpoint.Environment.Website.DeletedAt == null && endpoint.Environment.Website.IsEnabled,
            endpoint.Environment.Website.Client.DeletedAt == null && endpoint.Environment.Website.Client.IsActive,
            endpoint.Monitors.Any(monitor => monitor.DeletedAt == null)));

    public static IQueryable<Endpoint> Apply(IQueryable<Endpoint> endpoints) =>
        ApplyTestable(endpoints)
            .Where(endpoint => endpoint.Monitors.Any(monitor =>
                monitor.DeletedAt == null && monitor.SchedulingEnabled && monitor.IsEnabled));
}

internal sealed record EndpointTestReadiness(
    bool EndpointEnabled,
    bool EnvironmentAvailable,
    bool WebsiteEnabled,
    bool ClientActive,
    bool HasMonitor)
{
    public EndpointTestBlock Block => this switch
    {
        { EndpointEnabled: false } => EndpointTestBlock.EndpointDisabled,
        { EnvironmentAvailable: false } => EndpointTestBlock.EnvironmentArchived,
        { WebsiteEnabled: false } => EndpointTestBlock.WebsiteDisabled,
        { ClientActive: false } => EndpointTestBlock.ClientInactive,
        { HasMonitor: false } => EndpointTestBlock.NoMonitor,
        _ => EndpointTestBlock.None
    };
}

internal sealed class MonitoringEligibilityService(ApplicationDbContext dbContext) : IMonitoringEligibilityService
{
    public Task<bool> IsEndpointEligibleAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        MonitoringEligibility.Apply(dbContext.Endpoints.AsNoTracking())
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);

    public Task<bool> IsEndpointTestableAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        MonitoringEligibility.ApplyTestable(dbContext.Endpoints.AsNoTracking())
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Maintenance;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceReader(ApplicationDbContext dbContext) : IMaintenanceReader
{
    public async Task<IReadOnlyList<MaintenanceWindowListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var windows = await dbContext.MaintenanceWindows.AsNoTracking().Include(item => item.Targets).Include(item => item.Occurrences)
            .OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        return windows.Select(item =>
        {
            var target = item.Targets.Single();
            var occurrence = item.Occurrences.Single();
            return new MaintenanceWindowListItem(item.Id, ScopeLabel(target), occurrence.StartsAt, occurrence.EndsAt, item.TimezoneId,
                item.SuppressionPolicy, item.PauseEscalation, item.DeletedAt is not null, item.Version);
        }).ToArray();
    }

    public async Task<MaintenanceWindowDetails?> FindAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.MaintenanceWindows.AsNoTracking().Include(window => window.Targets).Include(window => window.Occurrences)
            .SingleOrDefaultAsync(window => window.Id == maintenanceWindowId, cancellationToken);
        if (item is null) return null;
        var target = item.Targets.Single();
        var occurrence = item.Occurrences.Single();
        return new MaintenanceWindowDetails(item.Id, ToScope(target), ScopeLabel(target), occurrence.StartsAt, occurrence.EndsAt,
            item.TimezoneId, item.Reason, item.SuppressionPolicy, item.PauseEscalation, item.ContinueFailureCounter,
            item.DeletedAt is not null, item.Version);
    }

    public async Task<IReadOnlyList<MaintenanceScopeOption>> ListScopeOptionsAsync(CancellationToken cancellationToken = default)
    {
        var clients = await dbContext.Clients.AsNoTracking().Where(item => item.DeletedAt == null && item.IsActive)
            .Select(item => new MaintenanceScopeOption(MaintenanceScopeKind.Client, item.Id, $"Client · {item.Name}")).ToArrayAsync(cancellationToken);
        var websites = await dbContext.Websites.AsNoTracking().Where(item => item.DeletedAt == null && item.IsEnabled)
            .Select(item => new MaintenanceScopeOption(MaintenanceScopeKind.Website, item.Id, $"Website · {item.Client.Name} / {item.Name}")).ToArrayAsync(cancellationToken);
        var environments = await dbContext.Environments.AsNoTracking().Where(item => item.DeletedAt == null && item.IsActive)
            .Select(item => new MaintenanceScopeOption(MaintenanceScopeKind.Environment, item.Id, $"Environment · {item.Website.Name} / {item.Name}")).ToArrayAsync(cancellationToken);
        var endpoints = await dbContext.Endpoints.AsNoTracking().Where(item => item.DeletedAt == null && item.IsEnabled)
            .Select(item => new MaintenanceScopeOption(MaintenanceScopeKind.Endpoint, item.Id, $"Endpoint · {item.DisplayUrl}")).ToArrayAsync(cancellationToken);
        var monitors = await dbContext.EndpointMonitors.AsNoTracking().Where(item => item.DeletedAt == null && item.IsEnabled)
            .Select(item => new MaintenanceScopeOption(MaintenanceScopeKind.Monitor, item.Id, $"Monitor · {item.Endpoint.DisplayUrl} ({item.MonitorType})")).ToArrayAsync(cancellationToken);
        return clients.Concat(websites).Concat(environments).Concat(endpoints).Concat(monitors).OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static MaintenanceScope ToScope(MaintenanceTarget target) => target.ClientId is { } id ? new(MaintenanceScopeKind.Client, id) : target.WebsiteId is { } websiteId ? new(MaintenanceScopeKind.Website, websiteId) : target.EnvironmentId is { } environmentId ? new(MaintenanceScopeKind.Environment, environmentId) : target.EndpointId is { } endpointId ? new(MaintenanceScopeKind.Endpoint, endpointId) : new(MaintenanceScopeKind.Monitor, target.EndpointMonitorId!.Value);
    private static string ScopeLabel(MaintenanceTarget target) => $"{ToScope(target).Kind} · {ToScope(target).TargetId}";
}

using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Maintenance;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceReader(ApplicationDbContext dbContext, TimeProvider timeProvider) : IMaintenanceReader
{
    public async Task<IReadOnlyList<MaintenanceWindowListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        // The window carries its schedule specification, so the list never loads a recurring
        // window's materialised occurrences; only the next one ahead of now is read.
        var now = timeProvider.GetUtcNow();
        var windows = await dbContext.MaintenanceWindows.AsNoTracking().Include(item => item.Targets)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new
            {
                Window = item,
                NextOccurrenceStartsAt = item.Occurrences
                    .Where(occurrence => occurrence.EndsAt > now)
                    .Min(occurrence => (DateTimeOffset?)occurrence.StartsAt)
            })
            .ToArrayAsync(cancellationToken);
        return windows.Select(row => new MaintenanceWindowListItem(
            row.Window.Id, ScopeLabel(row.Window.Targets.Single()), row.Window.ScheduleStartsAt,
            ScheduleEndsAt(row.Window), row.Window.TimezoneId, row.Window.SuppressionPolicy,
            row.Window.PauseEscalation, row.Window.DeletedAt is not null, ToRecurrence(row.Window),
            row.NextOccurrenceStartsAt, row.Window.Version)).ToArray();
    }

    public async Task<MaintenanceWindowDetails?> FindAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var row = await dbContext.MaintenanceWindows.AsNoTracking().Include(window => window.Targets)
            .Where(window => window.Id == maintenanceWindowId)
            .Select(window => new
            {
                Window = window,
                OccurrenceCount = window.Occurrences.Count,
                NextOccurrenceStartsAt = window.Occurrences
                    .Where(occurrence => occurrence.EndsAt > now)
                    .Min(occurrence => (DateTimeOffset?)occurrence.StartsAt)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var target = row.Window.Targets.Single();
        return new MaintenanceWindowDetails(row.Window.Id, ToScope(target), ScopeLabel(target),
            row.Window.ScheduleStartsAt, ScheduleEndsAt(row.Window), row.Window.TimezoneId, row.Window.Reason,
            row.Window.SuppressionPolicy, row.Window.PauseEscalation, row.Window.ContinueFailureCounter,
            row.Window.DeletedAt is not null, ToRecurrence(row.Window), row.NextOccurrenceStartsAt,
            row.OccurrenceCount, row.Window.Version);
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

    private static DateTimeOffset ScheduleEndsAt(MaintenanceWindow window) =>
        window.ScheduleStartsAt.AddSeconds(window.ScheduleDurationSeconds);

    private static MaintenanceRecurrenceSpec ToRecurrence(MaintenanceWindow window) =>
        new(window.RecurrencePattern, window.RecurrenceDaysOfWeek, window.RecurrenceUntil);

    private static MaintenanceScope ToScope(MaintenanceTarget target) => target.ClientId is { } id ? new(MaintenanceScopeKind.Client, id) : target.WebsiteId is { } websiteId ? new(MaintenanceScopeKind.Website, websiteId) : target.EnvironmentId is { } environmentId ? new(MaintenanceScopeKind.Environment, environmentId) : target.EndpointId is { } endpointId ? new(MaintenanceScopeKind.Endpoint, endpointId) : new(MaintenanceScopeKind.Monitor, target.EndpointMonitorId!.Value);
    private static string ScopeLabel(MaintenanceTarget target) => $"{ToScope(target).Kind} · {ToScope(target).TargetId}";
}

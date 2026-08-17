namespace WebHealth.Application.Maintenance;

public interface IMaintenanceReader
{
    Task<IReadOnlyList<MaintenanceWindowListItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<MaintenanceWindowDetails?> FindAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceScopeOption>> ListScopeOptionsAsync(CancellationToken cancellationToken = default);
}

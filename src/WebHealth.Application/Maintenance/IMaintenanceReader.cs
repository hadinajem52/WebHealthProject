namespace WebHealth.Application.Maintenance;

public interface IMaintenanceReader
{
    Task<MaintenanceWindowListPage> ListAsync(bool archivedOnly = false, CancellationToken cancellationToken = default);
    Task<MaintenanceWindowDetails?> FindAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceScopeOption>> ListScopeOptionsAsync(CancellationToken cancellationToken = default);
}

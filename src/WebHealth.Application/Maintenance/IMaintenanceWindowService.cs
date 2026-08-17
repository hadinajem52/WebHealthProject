using WebHealth.Application.Registry;
namespace WebHealth.Application.Maintenance;

public interface IMaintenanceWindowService
{
    Task<MaintenanceMutationResult> CreateAsync(CreateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<MaintenanceMutationResult> UpdateAsync(UpdateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<MaintenanceMutationResult> CancelAsync(CancelMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default);
}

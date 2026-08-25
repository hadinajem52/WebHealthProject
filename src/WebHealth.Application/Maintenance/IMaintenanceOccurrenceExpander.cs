namespace WebHealth.Application.Maintenance;

public sealed record MaintenanceExpansionResult(int WindowsExpanded, int OccurrencesCreated);

public interface IMaintenanceOccurrenceExpander
{
    Task<int> ExpandWindowAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default);

    Task<MaintenanceExpansionResult> ExpandDueAsync(CancellationToken cancellationToken = default);
}

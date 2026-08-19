namespace WebHealth.Application.Maintenance;

public sealed record MaintenanceExpansionResult(int WindowsExpanded, int OccurrencesCreated);

/// <summary>
/// Materialises recurring maintenance windows into concrete occurrence rows (BR-M05). Expansion is
/// idempotent on (window, occurrence start): re-running it over the same horizon writes nothing,
/// and extending the horizon appends without rewriting history.
/// </summary>
public interface IMaintenanceOccurrenceExpander
{
    Task<int> ExpandWindowAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default);

    Task<MaintenanceExpansionResult> ExpandDueAsync(CancellationToken cancellationToken = default);
}

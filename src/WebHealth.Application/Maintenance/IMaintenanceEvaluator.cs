namespace WebHealth.Application.Maintenance;

public interface IMaintenanceEvaluator
{
    Task<ActiveMaintenanceOccurrence?> FindActiveAsync(Guid endpointMonitorId, DateTimeOffset at, CancellationToken cancellationToken = default);
}

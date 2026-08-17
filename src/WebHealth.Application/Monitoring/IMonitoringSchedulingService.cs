namespace WebHealth.Application.Monitoring;

public interface IMonitoringSchedulingService
{
    Task<MonitoringDispatchResult> DispatchDueAsync(
        CancellationToken cancellationToken = default);

    Task<MonitoringDispatchResult> ReconcileAsync(
        CancellationToken cancellationToken = default);
}

public sealed record MonitoringDispatchResult(int ClaimedCount, int EnqueuedCount);

public interface ILogicalCheckQueue
{
    string Enqueue(Guid logicalCheckId, Guid durableWorkId);
}

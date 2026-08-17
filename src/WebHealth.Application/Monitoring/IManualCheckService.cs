using WebHealth.Application.Registry;

namespace WebHealth.Application.Monitoring;

public interface IManualCheckService
{
    Task<ManualCheckResult> RunNowAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public enum ManualCheckStatus
{
    Queued,
    Forbidden,
    MonitorNotAvailable,
    SchedulingUnavailable
}

public sealed record ManualCheckResult(ManualCheckStatus Status, Guid? LogicalCheckId = null)
{
    public static ManualCheckResult Queued(Guid logicalCheckId) => new(ManualCheckStatus.Queued, logicalCheckId);
    public static ManualCheckResult Forbidden() => new(ManualCheckStatus.Forbidden);
    public static ManualCheckResult MonitorNotAvailable() => new(ManualCheckStatus.MonitorNotAvailable);
    public static ManualCheckResult SchedulingUnavailable() => new(ManualCheckStatus.SchedulingUnavailable);
}

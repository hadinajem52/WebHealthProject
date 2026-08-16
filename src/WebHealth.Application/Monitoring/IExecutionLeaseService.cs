namespace WebHealth.Application.Monitoring;

public sealed record AcquireExecutionLease(
    Guid EndpointMonitorId,
    Guid LogicalCheckId,
    Guid OwnerToken,
    TimeSpan LeaseDuration);

public sealed record ExecutionLeaseClaim(
    Guid EndpointMonitorId,
    Guid LogicalCheckId,
    Guid OwnerToken,
    long FencingGeneration);

public interface IExecutionLeaseService
{
    Task<ExecutionLeaseClaim?> TryAcquireAsync(
        AcquireExecutionLease request,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        ExecutionLeaseClaim claim,
        CancellationToken cancellationToken = default);
}

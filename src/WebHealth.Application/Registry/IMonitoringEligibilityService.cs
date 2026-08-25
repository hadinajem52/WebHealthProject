namespace WebHealth.Application.Registry;

public interface IMonitoringEligibilityService
{
    Task<bool> IsEndpointEligibleAsync(Guid endpointId, CancellationToken cancellationToken = default);

    Task<bool> IsEndpointTestableAsync(Guid endpointId, CancellationToken cancellationToken = default);
}

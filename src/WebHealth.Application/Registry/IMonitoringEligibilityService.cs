namespace WebHealth.Application.Registry;

public interface IMonitoringEligibilityService
{
    /// <summary>Eligible for scheduled dispatch. False while the monitor is paused.</summary>
    Task<bool> IsEndpointEligibleAsync(Guid endpointId, CancellationToken cancellationToken = default);

    /// <summary>Eligible for an on-demand run. Unaffected by a paused monitor.</summary>
    Task<bool> IsEndpointTestableAsync(Guid endpointId, CancellationToken cancellationToken = default);
}

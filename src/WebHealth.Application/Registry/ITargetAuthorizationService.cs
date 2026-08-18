namespace WebHealth.Application.Registry;

public interface ITargetAuthorizationService
{
    Task<bool> CanTestEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of <paramref name="endpointIds" /> the caller may test.
    /// List views use this instead of asking per row.
    /// </summary>
    Task<IReadOnlySet<Guid>> FilterTestableEndpointsAsync(
        IReadOnlyCollection<Guid> endpointIds,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

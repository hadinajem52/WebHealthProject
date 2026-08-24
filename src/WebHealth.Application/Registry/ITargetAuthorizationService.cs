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

    /// <summary>
    /// Why <see cref="CanTestEndpointAsync" /> would refuse this endpoint, so a page can say what
    /// is missing instead of silently hiding its run button.
    /// </summary>
    Task<EndpointTestBlock> DescribeTestBlockAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public enum EndpointTestBlock
{
    None,
    NotVisible,
    NotPermitted,
    EndpointDisabled,
    EnvironmentInactive,
    WebsiteDisabled,
    ClientInactive,
    NoMonitor,
    NoTargetAuthorization
}

namespace WebHealth.Application.Registry;

public interface ITargetAuthorizationService
{
    Task<bool> CanTestEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> FilterTestableEndpointsAsync(
        IReadOnlyCollection<Guid> endpointIds,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

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
    EnvironmentArchived,
    WebsiteDisabled,
    ClientInactive,
    NoMonitor,
    NoTargetAuthorization
}

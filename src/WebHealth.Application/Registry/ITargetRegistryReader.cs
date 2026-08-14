namespace WebHealth.Application.Registry;

public interface ITargetRegistryReader
{
    Task<IReadOnlyList<EnvironmentListItem>> ListEnvironmentsAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<EnvironmentDetails?> FindEnvironmentAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EndpointListItem>> ListEndpointsAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<EndpointDetails?> FindEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnvironmentListItem>> ListDeletedEnvironmentsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EndpointListItem>> ListDeletedEndpointsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

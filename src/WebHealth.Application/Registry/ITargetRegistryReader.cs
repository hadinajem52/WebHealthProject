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

    Task<IReadOnlyList<RegistryEndpointItem>> ListAllEndpointsAsync(
        RegistryAccessContext access,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<EndpointDetails?> FindEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Certificate status for one endpoint. Returns
    /// <see cref="CertificateStatus.NotApplicable" /> for an HTTP-only endpoint, which has no
    /// certificate to report on (BR-C01), and null when the endpoint is not visible.
    /// </summary>
    Task<CertificateStatus?> FindCertificateStatusAsync(
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

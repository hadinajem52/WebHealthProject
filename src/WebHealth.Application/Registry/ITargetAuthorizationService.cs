namespace WebHealth.Application.Registry;

public interface ITargetAuthorizationService
{
    Task<bool> CanTestEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

namespace WebHealth.Application.Registry;

public interface IClientRegistryService
{
    Task<RegistryMutationResult> CreateAsync(
        CreateClient command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> UpdateAsync(
        UpdateClient command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> DisableAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> DeleteAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> RestoreAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

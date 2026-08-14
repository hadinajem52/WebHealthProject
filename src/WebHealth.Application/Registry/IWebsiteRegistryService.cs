namespace WebHealth.Application.Registry;

public interface IWebsiteRegistryService
{
    Task<RegistryMutationResult> CreateAsync(
        CreateWebsite command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> UpdateAsync(
        UpdateWebsite command,
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

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

    /// <summary>
    /// Permanently removes an archived website, every environment under it, and every endpoint
    /// under those, with everything recorded about them. The audit trail is retained. There is
    /// no restore.
    /// </summary>
    Task<RegistryMutationResult> PurgeAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> RestoreAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

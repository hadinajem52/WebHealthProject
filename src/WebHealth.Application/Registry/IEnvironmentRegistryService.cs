namespace WebHealth.Application.Registry;

public interface IEnvironmentRegistryService
{
    Task<RegistryMutationResult> CreateAsync(CreateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> UpdateAsync(UpdateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);
}

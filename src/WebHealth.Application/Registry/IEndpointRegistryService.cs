namespace WebHealth.Application.Registry;

public interface IEndpointRegistryService
{
    Task<RegistryMutationResult> CreateAsync(CreateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> UpdateAsync(UpdateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);

    /// <summary>Stops scheduled dispatch for the endpoint. Manual runs stay available.</summary>
    Task<RegistryMutationResult> PauseScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);

    /// <summary>Returns the endpoint to its monitoring cadence.</summary>
    Task<RegistryMutationResult> ResumeScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default);
}

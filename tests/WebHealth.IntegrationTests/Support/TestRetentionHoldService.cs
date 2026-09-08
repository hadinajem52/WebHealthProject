using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;

namespace WebHealth.IntegrationTests.Support;

internal sealed class TestRetentionHoldService : IRetentionHoldService
{
    public CreateRetentionHold? LastCreated { get; private set; }
    public Task<IReadOnlyList<RetentionHoldView>> ListAsync(RegistryAccessContext access, int offset = 0,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RetentionHoldView>>(
        [new(new Guid("fa1c5db2-7b60-4865-bde3-e101050dfeee"), "Endpoint", Guid.Empty, "<script>alert('hold')</script>",
            access.UserId, DateTimeOffset.UtcNow, null, null, null)]);
    public Task<RegistryMutationResult> CreateAsync(CreateRetentionHold command, RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        LastCreated = command;
        return Task.FromResult(RegistryMutationResult.Success(Guid.NewGuid()));
    }
    public Task<RegistryMutationResult> ReleaseAsync(Guid holdId, RegistryAccessContext access,
        CancellationToken cancellationToken = default) => Task.FromResult(RegistryMutationResult.Success(holdId));
}

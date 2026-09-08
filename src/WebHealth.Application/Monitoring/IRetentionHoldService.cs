using WebHealth.Application.Registry;

namespace WebHealth.Application.Monitoring;

public sealed record CreateRetentionHold(string ScopeType, Guid ScopeId, string Reason, DateTimeOffset? ExpiresAt);

public sealed record RetentionHoldView(Guid Id, string ScopeType, Guid ScopeId, string Reason,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt,
    Guid? ReleasedByUserId, DateTimeOffset? ReleasedAt);

public interface IRetentionHoldService
{
    Task<IReadOnlyList<RetentionHoldView>> ListAsync(RegistryAccessContext access, int offset = 0,
        CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> CreateAsync(CreateRetentionHold command, RegistryAccessContext access,
        CancellationToken cancellationToken = default);
    Task<RegistryMutationResult> ReleaseAsync(Guid holdId, RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

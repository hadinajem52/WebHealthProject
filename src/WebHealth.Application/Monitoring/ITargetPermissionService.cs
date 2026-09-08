using WebHealth.Application.Registry;

namespace WebHealth.Application.Monitoring;

public interface ITargetPermissionService
{
    Task<TargetPermissions?> ReadAsync(Guid endpointId, RegistryAccessContext access, CancellationToken token);
    Task<RegistryMutationResult> GrantAsync(GrantTargetPermission command, RegistryAccessContext access, CancellationToken token);
    Task<RegistryMutationResult> RevokeAsync(Guid endpointId, Guid permissionId, string reason, RegistryAccessContext access, CancellationToken token);
}

public sealed record GrantTargetPermission(Guid EndpointId, string Url, string Kind, string EvidenceReference, DateTimeOffset? ExpiresAt);
public sealed record TargetPermissions(Guid EndpointId, string Url, IReadOnlyList<TargetPermissionItem> Items);
public sealed record TargetPermissionItem(Guid Id, string Host, int Port, string Kind, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt);

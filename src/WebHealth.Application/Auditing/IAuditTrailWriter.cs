namespace WebHealth.Application.Auditing;

public interface IAuditTrailWriter
{
    Task RecordUserCreatedAsync(
        AuditWriteContext context,
        UserAuditSnapshot after,
        CancellationToken cancellationToken = default);

    Task RecordUserUpdatedAsync(
        AuditWriteContext context,
        UserAuditSnapshot before,
        UserAuditSnapshot after,
        CancellationToken cancellationToken = default);

    Task RecordTeamCreatedAsync(
        AuditWriteContext context,
        TeamAuditSnapshot after,
        CancellationToken cancellationToken = default);

    Task RecordTeamUpdatedAsync(
        AuditWriteContext context,
        TeamAuditSnapshot before,
        TeamAuditSnapshot after,
        CancellationToken cancellationToken = default);

    Task RecordClientMutationAsync(
        AuditWriteContext context,
        ClientAuditAction action,
        ClientAuditSnapshot? before,
        ClientAuditSnapshot after,
        CancellationToken cancellationToken = default);

    Task RecordWebsiteMutationAsync(
        AuditWriteContext context,
        WebsiteAuditAction action,
        WebsiteAuditSnapshot? before,
        WebsiteAuditSnapshot after,
        CancellationToken cancellationToken = default);
}

public sealed record AuditWriteContext(Guid ActorUserId, DateTimeOffset OccurredAt);

public sealed record UserAuditSnapshot(
    Guid UserId,
    string DisplayName,
    string Email,
    bool IsDisabled,
    IReadOnlyList<string> Roles,
    bool PasswordReset);

public sealed record TeamAuditSnapshot(
    Guid TeamId,
    string Name,
    bool IsDisabled,
    IReadOnlyList<Guid> MemberUserIds);

public enum ClientAuditAction
{
    Created,
    Updated,
    Disabled,
    Deleted,
    Restored
}

public sealed record ClientAuditSnapshot(
    Guid ClientId,
    string Name,
    Guid OwnerSubjectId,
    bool IsActive,
    bool IsDeleted,
    bool NotesChanged,
    long Version);

public enum WebsiteAuditAction
{
    Created,
    Updated,
    Disabled,
    Deleted,
    Restored
}

public sealed record WebsiteAuditSnapshot(
    Guid WebsiteId,
    Guid ClientId,
    string Name,
    Guid OwnerSubjectId,
    string? TechnologyCms,
    bool IsEnabled,
    bool IsDeleted,
    long Version);

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

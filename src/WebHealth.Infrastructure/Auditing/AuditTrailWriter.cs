using System.Text.Json;
using WebHealth.Application.Auditing;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Auditing;

public sealed class AuditTrailWriter(ApplicationDbContext dbContext) : IAuditTrailWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task RecordUserCreatedAsync(
        AuditWriteContext context,
        UserAuditSnapshot after,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, "user.created", "user", after.UserId, null, after, cancellationToken);

    public Task RecordUserUpdatedAsync(
        AuditWriteContext context,
        UserAuditSnapshot before,
        UserAuditSnapshot after,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, "user.updated", "user", after.UserId, before, after, cancellationToken);

    public Task RecordTeamCreatedAsync(
        AuditWriteContext context,
        TeamAuditSnapshot after,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, "team.created", "team", after.TeamId, null, after, cancellationToken);

    public Task RecordTeamUpdatedAsync(
        AuditWriteContext context,
        TeamAuditSnapshot before,
        TeamAuditSnapshot after,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, "team.updated", "team", after.TeamId, before, after, cancellationToken);

    private async Task RecordAsync<TSnapshot>(
        AuditWriteContext context,
        string action,
        string entityType,
        Guid entityId,
        TSnapshot? before,
        TSnapshot after,
        CancellationToken cancellationToken)
        where TSnapshot : class
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = context.ActorUserId,
            ActorIdentifier = context.ActorUserId.ToString(),
            OccurredAt = context.OccurredAt,
            Action = action,
            EntityType = entityType,
            EntityIdentifier = entityId.ToString(),
            Outcome = "succeeded",
            BeforeValues = before is null ? null : JsonSerializer.Serialize(before, SerializerOptions),
            AfterValues = JsonSerializer.Serialize(after, SerializerOptions)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

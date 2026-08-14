using WebHealth.Application.Auditing;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Auditing;

public sealed class AuthorizationDenialAuditWriter(ApplicationDbContext dbContext)
    : IAuthorizationDenialAuditWriter
{
    public async Task WriteAsync(
        AuthorizationDenialAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = entry.ActorUserId,
            ActorIdentifier = entry.ActorUserId?.ToString() ?? "unknown",
            OccurredAt = entry.OccurredAt,
            Action = "authorization.denied",
            EntityType = "http_request",
            EntityIdentifier = entry.RequestPath,
            Outcome = "forbidden",
            RequestMethod = entry.RequestMethod,
            CorrelationId = entry.CorrelationId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

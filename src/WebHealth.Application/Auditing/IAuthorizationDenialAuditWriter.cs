namespace WebHealth.Application.Auditing;

public interface IAuthorizationDenialAuditWriter
{
    Task WriteAsync(AuthorizationDenialAuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed record AuthorizationDenialAuditEntry(
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string RequestMethod,
    string RequestPath,
    string CorrelationId);

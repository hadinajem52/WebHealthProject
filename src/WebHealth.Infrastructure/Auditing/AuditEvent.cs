namespace WebHealth.Infrastructure.Auditing;

public sealed class AuditEvent
{
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }

    public required string ActorIdentifier { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public required string EntityIdentifier { get; set; }

    public required string Outcome { get; set; }

    public string? RequestMethod { get; set; }

    public string? CorrelationId { get; set; }

    public string? BeforeValues { get; set; }

    public string? AfterValues { get; set; }
}

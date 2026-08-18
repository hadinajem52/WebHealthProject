using WebHealth.Infrastructure.Incidents;

namespace WebHealth.Infrastructure.Notifications;

public sealed class NotificationEvent
{
    public Guid Id { get; set; }
    public Guid? IncidentEventId { get; set; }
    public Guid IncidentId { get; set; }
    public required string SourceKind { get; set; }
    public required string EventType { get; set; }
    public required string OccurrenceKey { get; set; }
    public required string TemplateVersion { get; set; }
    public bool IsSuppressed { get; set; }
    public string? SuppressionReason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Incident Incident { get; set; } = null!;
    public IncidentEvent? IncidentEvent { get; set; }
    public ICollection<NotificationDelivery> Deliveries { get; } = [];
}

public sealed class NotificationDelivery
{
    public Guid Id { get; set; }
    public Guid NotificationEventId { get; set; }
    public required string Channel { get; set; }
    public required string NormalizedRecipient { get; set; }
    public short RecipientNormalizationVersion { get; set; }
    public required string State { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public NotificationEvent NotificationEvent { get; set; } = null!;
    public ICollection<NotificationAttempt> Attempts { get; } = [];
}

public sealed class NotificationAttempt
{
    public Guid Id { get; set; }
    public Guid NotificationDeliveryId { get; set; }
    public int AttemptNumber { get; set; }
    public required string TransportOutcome { get; set; }
    public string? SafeResponse { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public NotificationDelivery Delivery { get; set; } = null!;
}

/// <summary>
/// When a user last marked their in-app notification feed read. One row per user keeps the
/// unread indicator cheap: no per-notification read rows to write or prune.
/// </summary>
public sealed class NotificationReadMarker
{
    public Guid UserId { get; set; }
    public DateTimeOffset LastReadAt { get; set; }
    public long Version { get; set; }
}

namespace WebHealth.Application.Notifications;

public sealed record NotificationFeedItem(
    Guid IncidentId,
    string EventType,
    string Severity,
    string IncidentStatus,
    string EndpointDisplayUrl,
    bool IsSuppressed,
    bool IsUnread,
    DateTimeOffset OccurredAt);

public sealed record NotificationFeed(IReadOnlyList<NotificationFeedItem> Items, int UnreadCount)
{
    public static NotificationFeed Empty { get; } = new([], 0);

    public static NotificationFeed Unavailable { get; } = new([], 0) { IsUnavailable = true };

    public bool IsUnavailable { get; init; }
}

public interface INotificationFeedReader
{
    Task<NotificationFeed> GetForRecipientAsync(
        Guid userId,
        string? emailAddress,
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

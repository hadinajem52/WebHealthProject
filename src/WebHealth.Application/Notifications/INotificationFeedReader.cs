namespace WebHealth.Application.Notifications;

/// <summary>
/// One notification that was addressed to the signed-in user, with the current state of
/// the incident it concerns (which may have moved on since the notification was raised).
/// </summary>
public sealed record NotificationFeedItem(
    Guid IncidentId,
    string EventType,
    string Severity,
    string IncidentStatus,
    string EndpointDisplayUrl,
    bool IsSuppressed,
    bool IsUnread,
    DateTimeOffset OccurredAt);

/// <param name="UnreadCount">
/// Notifications raised after the reader last marked their feed read. Drives the header dot.
/// </param>
public sealed record NotificationFeed(IReadOnlyList<NotificationFeedItem> Items, int UnreadCount);

public interface INotificationFeedReader
{
    /// <summary>
    /// Recent notifications addressed to <paramref name="emailAddress" />. Recipients are matched
    /// on the stored normalized address, so this agrees with what the email dispatcher sent.
    /// </summary>
    Task<NotificationFeed> GetForRecipientAsync(
        Guid userId,
        string? emailAddress,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Marks everything currently raised for this reader as read.</summary>
    Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

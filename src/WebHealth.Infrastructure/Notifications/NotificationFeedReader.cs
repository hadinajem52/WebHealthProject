using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Notifications;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Notifications;

/// <summary>
/// Reads the notifications already addressed to a recipient by the dispatcher, so the in-app
/// panel and the outbound email agree on who was told what. Recipients are matched on the
/// stored normalized address rather than re-resolving ownership.
/// </summary>
internal sealed class NotificationFeedReader(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : INotificationFeedReader
{
    private static readonly NotificationFeed Empty = new([], 0);

    public async Task<NotificationFeed> GetForRecipientAsync(
        Guid userId,
        string? emailAddress,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var recipient = RecipientNormalizer.Normalize(emailAddress);
        if (recipient is null)
        {
            return Empty;
        }

        // A user who has never opened the panel has no marker, so everything counts as unread.
        var lastReadAt = await dbContext.NotificationReadMarkers.AsNoTracking()
            .Where(marker => marker.UserId == userId)
            .Select(marker => (DateTimeOffset?)marker.LastReadAt)
            .SingleOrDefaultAsync(cancellationToken);

        var addressed = dbContext.NotificationDeliveries.AsNoTracking()
            .Where(delivery => delivery.NormalizedRecipient == recipient)
            .Select(delivery => delivery.NotificationEvent);

        var items = await addressed
            .OrderByDescending(notification => notification.OccurredAt)
            .Take(limit)
            .Select(notification => new NotificationFeedItem(
                notification.IncidentId,
                notification.EventType,
                notification.Incident.Severity,
                notification.Incident.Status,
                notification.Incident.EndpointMonitor.Endpoint.DisplayUrl,
                notification.IsSuppressed,
                lastReadAt == null || notification.OccurredAt > lastReadAt,
                notification.OccurredAt))
            .ToListAsync(cancellationToken);

        // Counted over the whole feed, not just the page above, so the dot does not clear itself
        // when older unread items fall past the display limit.
        var unreadCount = await addressed
            .Where(notification => lastReadAt == null || notification.OccurredAt > lastReadAt)
            .CountAsync(cancellationToken);

        return new(items, unreadCount);
    }

    public async Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var marker = await dbContext.NotificationReadMarkers
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (marker is null)
        {
            dbContext.NotificationReadMarkers.Add(new NotificationReadMarker
            {
                UserId = userId,
                LastReadAt = now,
                Version = 1
            });
        }
        else
        {
            marker.LastReadAt = now;
            marker.Version++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

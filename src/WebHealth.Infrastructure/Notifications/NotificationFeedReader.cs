using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Notifications;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Notifications;

internal sealed class NotificationFeedReader(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : INotificationFeedReader
{
    private const int MaximumLimit = 50;

    public async Task<NotificationFeed> GetForRecipientAsync(
        Guid userId,
        string? emailAddress,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var recipient = RecipientNormalizer.Normalize(emailAddress);
        if (recipient is null)
        {
            return NotificationFeed.Empty;
        }

        limit = Math.Clamp(limit, 1, MaximumLimit);

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

        var unreadCount = await addressed
            .Where(notification => lastReadAt == null || notification.OccurredAt > lastReadAt)
            .CountAsync(cancellationToken);

        return new(items, unreadCount);
    }

    public Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO web_health.notification_read_marker (user_id, last_read_at, version)
             VALUES ({userId}, {now}, 1)
             ON CONFLICT (user_id) DO UPDATE
             SET last_read_at = GREATEST(web_health.notification_read_marker.last_read_at, EXCLUDED.last_read_at),
                 version = web_health.notification_read_marker.version + 1
             """,
            cancellationToken);
    }
}

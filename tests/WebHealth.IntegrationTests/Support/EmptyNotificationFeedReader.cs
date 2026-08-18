using WebHealth.Application.Notifications;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// Stands in for the notification feed in tests that run without a database, so shell and
/// authorization coverage does not depend on notification storage.
/// </summary>
internal sealed class EmptyNotificationFeedReader : INotificationFeedReader
{
    public Task<NotificationFeed> GetForRecipientAsync(
        Guid userId,
        string? emailAddress,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NotificationFeed([], 0));

    public Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

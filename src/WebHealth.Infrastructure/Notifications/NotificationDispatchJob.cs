using Hangfire;

namespace WebHealth.Infrastructure.Notifications;

internal sealed class NotificationDispatchJob(
    NotificationDispatchService dispatchService,
    NotificationReminderService reminderService)
{
    [Queue(NotificationQueueNames.Notifications)]
    [AutomaticRetry(Attempts = 0)]
    public async Task DispatchAsync(CancellationToken cancellationToken) =>
        await dispatchService.DispatchDueAsync(cancellationToken);

    [Queue(NotificationQueueNames.Notifications)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SweepRemindersAsync(CancellationToken cancellationToken) =>
        await reminderService.SweepAsync(cancellationToken);
}

using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace WebHealth.Infrastructure.Notifications;

public static class NotificationSchedulingApplicationBuilderExtensions
{
    public static WebApplication UseNotificationScheduling(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<NotificationSchedulingOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        var recurringOptions = new RecurringJobOptions();
        recurringJobs.AddOrUpdate<NotificationDispatchJob>(
            "notification-dispatch",
            NotificationQueueNames.Notifications,
            job => job.DispatchAsync(CancellationToken.None),
            Cron.Minutely,
            recurringOptions);
        recurringJobs.AddOrUpdate<NotificationDispatchJob>(
            "notification-reminder-sweep",
            NotificationQueueNames.Notifications,
            job => job.SweepRemindersAsync(CancellationToken.None),
            Cron.Minutely,
            recurringOptions);
        return app;
    }
}

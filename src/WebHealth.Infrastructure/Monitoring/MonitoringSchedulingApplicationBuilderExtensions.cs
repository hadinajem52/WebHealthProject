using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Maintenance;

namespace WebHealth.Infrastructure.Monitoring;

public static class MonitoringSchedulingApplicationBuilderExtensions
{
    public static WebApplication UseMonitoringRetention(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<MonitoringRetentionOptions>().Enabled) return app;
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<MonitoringRetentionJob>(
            "monitoring-retention", MaintenanceQueueNames.Maintenance,
            job => job.ExecuteAsync(CancellationToken.None), Cron.Hourly, new RecurringJobOptions());
        return app;
    }

    public static WebApplication UseMonitoringScheduling(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<MonitoringSchedulingOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        var recurringOptions = new RecurringJobOptions();
        recurringJobs.AddOrUpdate<MonitoringDispatchJob>(
            "monitoring-dispatch",
            MonitoringQueueNames.ShortChecks,
            job => job.DispatchAsync(CancellationToken.None),
            Cron.Minutely,
            recurringOptions);
        recurringJobs.AddOrUpdate<MonitoringDispatchJob>(
            "monitoring-reconciliation",
            MonitoringQueueNames.ShortChecks,
            job => job.ReconcileAsync(CancellationToken.None),
            Cron.Minutely,
            recurringOptions);
        return app;
    }
}

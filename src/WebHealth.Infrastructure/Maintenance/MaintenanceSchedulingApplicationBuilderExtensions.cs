using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace WebHealth.Infrastructure.Maintenance;

public static class MaintenanceSchedulingApplicationBuilderExtensions
{
    public static WebApplication UseMaintenanceScheduling(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<MaintenanceSchedulingOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<MaintenanceExpansionJob>(
            "maintenance-occurrence-expansion",
            MaintenanceQueueNames.Maintenance,
            job => job.ExpandAsync(CancellationToken.None),
            Cron.Hourly(),
            new RecurringJobOptions());
        return app;
    }
}

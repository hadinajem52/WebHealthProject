using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.IntegrationTests.Support;

internal static class RetentionCoordinatorAssertions
{
    public static async Task VerifyAsync(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString,
            ["Monitoring:Retention:Enabled"] = "true",
            ["Monitoring:Retention:DryRun"] = "true"
        });
        builder.Services.AddInfrastructure(builder.Configuration);
        await using var app = builder.Build();
        var manager = app.Services.GetRequiredService<IRecurringJobManager>();
        string? triggeredJobId = null;
        try
        {
            app.UseMonitoringRetention();
            app.UseMonitoringRetention();
            using var connection = app.Services.GetRequiredService<JobStorage>().GetConnection();
            var registration = connection.GetAllEntriesFromHash("recurring-job:monitoring-retention");
            registration["Cron"].Should().Be("0 * * * *");
            registration["Job"].Should().Contain(nameof(MonitoringRetentionJob));
            connection.GetAllItemsFromSet("recurring-jobs").Count(id => id == "monitoring-retention").Should().Be(1);
            triggeredJobId = ((IRecurringJobManagerV2)manager).TriggerJob("monitoring-retention");
            var queued = connection.GetStateData(triggeredJobId);
            queued.Name.Should().Be("Enqueued");
            queued.Data["Queue"].Should().Be("maintenance");
            await using var scope = app.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<MonitoringRetentionCoordinator>().ExecuteAsync();
            result.DurationLimitReached.Should().BeFalse();
            result.Batches.Select(item => item.Category).Should().Equal(Enum.GetValues<RetentionCategory>());
            result.Batches.Should().OnlyContain(item => item.Selected == 0 && item.Deleted == 0);
        }
        finally
        {
            if (triggeredJobId is not null) app.Services.GetRequiredService<IBackgroundJobClient>().Delete(triggeredJobId);
            manager.RemoveIfExists("monitoring-retention");
        }
    }
}

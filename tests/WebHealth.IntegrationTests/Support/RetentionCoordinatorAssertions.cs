using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

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
        app.Urls.Add("http://127.0.0.1:0");
        var manager = app.Services.GetRequiredService<IRecurringJobManager>();
        string? triggeredJobId = null;
        var started = false;
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
            await using (var blockerScope = app.Services.CreateAsyncScope())
            {
                var blocker = blockerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await using var transaction = await blocker.Database.BeginTransactionAsync();
                await RetentionTransactionLock.AcquireAsync(blocker, CancellationToken.None);
                var bounded = new MonitoringRetentionCoordinator(
                    scope.ServiceProvider.GetRequiredService<IMonitoringRetentionBatchRunner>(),
                    new() { Enabled = true, DryRun = true, MaximumRunDuration = TimeSpan.FromSeconds(1) }, TimeProvider.System);
                var interrupted = await bounded.ExecuteAsync();
                interrupted.DurationLimitReached.Should().BeTrue();
                interrupted.Batches.Should().BeEmpty("a blocked transaction cannot complete a retention category");
                await transaction.RollbackAsync();
            }
            await app.StartAsync();
            started = true;
            using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var state = connection.GetStateData(triggeredJobId);
            while (state.Name is not ("Succeeded" or "Failed" or "Deleted"))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), completionDeadline.Token);
                state = connection.GetStateData(triggeredJobId);
            }
            state.Name.Should().Be("Succeeded", "the registered maintenance worker must activate and complete the retention job");
        }
        finally
        {
            if (started) await app.StopAsync();
            if (triggeredJobId is not null) app.Services.GetRequiredService<IBackgroundJobClient>().Delete(triggeredJobId);
            manager.RemoveIfExists("monitoring-retention");
        }
    }
}

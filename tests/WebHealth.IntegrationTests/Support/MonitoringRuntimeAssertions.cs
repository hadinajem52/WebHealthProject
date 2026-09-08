using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using Xunit;

namespace WebHealth.IntegrationTests.Support;

internal static class MonitoringRuntimeAssertions
{
    public static async Task VerifyAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<MonitoringRuntimeRecorder>();
        (await database.MonitoringRuntimeStates.CountAsync()).Should().Be(0, "migration must not fabricate heartbeat rows");
        var columns = await database.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name = 'monitoring_runtime_state'
            """).ToArrayAsync();
        columns.Should().BeEquivalentTo("operation", "invocation_id", "last_started_at", "last_succeeded_at",
            "last_failed_at", "last_duration_ms", "failure_category", "consecutive_failures");

        foreach (var operation in new[] { "monitoring-dispatch", "monitoring-reconciliation" })
        {
            await recorder.RunAsync(operation, () => Task.FromResult(new MonitoringDispatchResult(0, 0)), CancellationToken.None);
            var success = await ReadAsync(database, operation);
            success.LastSucceededAt.Should().NotBeNull("zero-work invocations are real successful heartbeats");
            success.LastFailedAt.Should().BeNull();
            success.ConsecutiveFailures.Should().Be(0);
            for (var failure = 1; failure <= 3; failure++)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RunAsync(operation,
                    () => throw new InvalidOperationException("untrusted failure text"), CancellationToken.None));
                var failed = await ReadAsync(database, operation);
                failed.ConsecutiveFailures.Should().Be(failure);
                failed.FailureCategory.Should().Be("Unexpected");
                failed.LastSucceededAt.Should().Be(success.LastSucceededAt);
                failed.LastFailedAt.Should().NotBeNull();
            }
            await recorder.RunAsync(operation, () => Task.FromResult(new MonitoringDispatchResult(1, 0)), CancellationToken.None);
            (await ReadAsync(database, operation)).FailureCategory.Should().Be("QueueEnqueue");
            await Assert.ThrowsAsync<OperationCanceledException>(() => recorder.RunAsync(operation,
                () => throw new OperationCanceledException(), CancellationToken.None));
            (await ReadAsync(database, operation)).FailureCategory.Should().Be("Cancellation");
            await recorder.RunAsync(operation, () => Task.FromResult(new MonitoringDispatchResult(0, 0)), CancellationToken.None);
            var recovered = await ReadAsync(database, operation);
            recovered.ConsecutiveFailures.Should().Be(0);
            recovered.FailureCategory.Should().BeNull();
            recovered.LastFailedAt.Should().NotBeNull("successful recovery retains the last failure timestamp");
        }

        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = recorder.RunAsync("monitoring-dispatch", async () =>
        {
            began.SetResult();
            await release.Task;
            return new MonitoringDispatchResult(1, 0);
        }, CancellationToken.None);
        await began.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await recorder.RunAsync("monitoring-dispatch", () => Task.FromResult(new MonitoringDispatchResult(0, 0)), CancellationToken.None);
        var newer = await ReadAsync(database, "monitoring-dispatch");
        release.SetResult();
        await older;
        (await ReadAsync(database, "monitoring-dispatch")).Should().BeEquivalentTo(newer,
            "an older invocation finishing late must not overwrite the latest invocation");
    }

    private static Task<MonitoringRuntimeState> ReadAsync(ApplicationDbContext database, string operation) =>
        database.MonitoringRuntimeStates.AsNoTracking().SingleAsync(state => state.Operation == operation);

    public static async Task VerifyUpgradeAsync(ApplicationDbContext database, MonitoringRuntimeRecorder recorder)
    {
        await RetentionHoldAssertions.VerifyUpgradeAsync(database);
        await recorder.RunAsync("monitoring-dispatch", () => Task.FromResult(new MonitoringDispatchResult(0, 0)), CancellationToken.None);
        await database.Database.MigrateAsync("SslPolicyFingerprint");
        await database.Database.MigrateAsync();
        (await database.MonitoringRuntimeStates.CountAsync()).Should().Be(0,
            "rollback drops runtime evidence and upgrade does not invent it");
        await recorder.RunAsync("monitoring-dispatch", () => Task.FromResult(new MonitoringDispatchResult(0, 0)), CancellationToken.None);
        var before = await ReadAsync(database, "monitoring-dispatch");
        await database.Database.MigrateAsync();
        (await ReadAsync(database, "monitoring-dispatch")).Should().BeEquivalentTo(before);
    }
}

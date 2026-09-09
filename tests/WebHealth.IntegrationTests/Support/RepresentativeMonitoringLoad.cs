using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WebHealth.Application.Administration;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class RepresentativeMonitoringLoad
{
    private const int EndpointCount = 500;
    private const int HttpsEndpointCount = 350;
    private const int ExpectedMonitorCount = 850;
    private static readonly DateTimeOffset AsOf = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    public static async Task VerifyAsync(string connectionString, string evidencePath, int memoryWindowMinutes)
    {
        memoryWindowMinutes.Should().BePositive();
        var clock = new MutableClock(AsOf);
        var queue = new ControlledQueue { FailOnCall = 250 };
        await using var services = BuildServices(connectionString, clock, queue);
        await SeedFleetAsync(services);

        MonitoringDispatchResult dispatch;
        TimeSpan dispatchDuration;
        await using (var scope = services.CreateAsyncScope())
        {
            var stopwatch = Stopwatch.StartNew();
            dispatch = await scope.ServiceProvider.GetRequiredService<IMonitoringSchedulingService>().DispatchDueAsync();
            dispatchDuration = stopwatch.Elapsed;
        }
        dispatch.Should().Be(new MonitoringDispatchResult(EndpointCount, EndpointCount - 1));

        ScheduleEvidence schedule;
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var checks = await database.LogicalChecks.AsNoTracking().OrderBy(item => item.ScheduledFor).ThenBy(item => item.Id)
                .Select(item => new { item.Id, item.ScheduledFor, item.CreatedAt }).ToArrayAsync();
            var queueAges = await database.DurableWork.AsNoTracking().OrderBy(item => item.Id)
                .Select(item => item.UpdatedAt - item.CreatedAt).ToArrayAsync();
            checks.Should().HaveCount(EndpointCount);
            queueAges.Should().HaveCount(EndpointCount);
            (await database.CheckConfigurationSnapshots.CountAsync()).Should().Be(EndpointCount);
            schedule = new(checks.Select(item => item.CreatedAt - item.ScheduledFor!.Value).Order().ToArray(),
                queueAges.Order().ToArray());
        }

        schedule.CreationLags[^1].Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
        Percentile95(schedule.CreationLags).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(90));
        Percentile95(schedule.QueueAges).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(2));
        schedule.QueueAges[^1].Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(3));
        MonitoringDispatchResult recovery;
        TimeSpan recoveryDuration;
        await using (var scope = services.CreateAsyncScope())
        {
            var stopwatch = Stopwatch.StartNew();
            recovery = await scope.ServiceProvider.GetRequiredService<IMonitoringSchedulingService>().ReconcileAsync();
            recoveryDuration = stopwatch.Elapsed;
        }
        recovery.Should().Be(new MonitoringDispatchResult(EndpointCount, EndpointCount));
        queue.Jobs.Select(item => item.DurableWorkId).Distinct().Should().HaveCount(EndpointCount);
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await database.LogicalChecks.CountAsync()).Should().Be(EndpointCount);
            (await database.DurableWork.CountAsync()).Should().Be(EndpointCount);
            (await database.DurableWork.CountAsync(item => item.State == "Enqueued")).Should().Be(EndpointCount);
            (await database.TargetAuthorizationEvidence.CountAsync(item => item.RevokedAt == null)).Should().Be(EndpointCount);
        }

        var limiter = new SafeHttpConcurrencyLimiter(new()
        {
            GlobalConcurrency = 100,
            PerHostConcurrency = 2,
            PerIpConcurrency = 4
        });
        await ExerciseConcurrencyAsync(limiter, TimeSpan.FromSeconds(5));
        var firstWindow = await MeasureMemoryWindowAsync(limiter, TimeSpan.FromMinutes(memoryWindowMinutes));
        var secondWindow = await MeasureMemoryWindowAsync(limiter, TimeSpan.FromMinutes(memoryWindowMinutes));
        firstWindow.MaximumConcurrency.Should().BeLessThanOrEqualTo(100);
        secondWindow.MaximumConcurrency.Should().BeLessThanOrEqualTo(100);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var versionCommand = new NpgsqlCommand("SELECT version()", connection);
        var postgresql = (string)(await versionCommand.ExecuteScalarAsync())!;
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        await File.WriteAllTextAsync(evidencePath, RenderEvidence(postgresql, dispatch, dispatchDuration, schedule,
            recovery, recoveryDuration, queue, firstWindow, secondWindow, memoryWindowMinutes), new UTF8Encoding(false));
    }

    public static async Task VerifyDatabaseRestartRecoveryAsync(string connectionString, string evidencePath)
    {
        var clock = new MutableClock(AsOf.AddMinutes(6));
        var queue = new ControlledQueue();
        await using var services = BuildServices(connectionString, clock, queue);
        MonitoringDispatchResult recovery;
        TimeSpan duration;
        await using (var scope = services.CreateAsyncScope())
        {
            var stopwatch = Stopwatch.StartNew();
            recovery = await scope.ServiceProvider.GetRequiredService<IMonitoringSchedulingService>().ReconcileAsync();
            duration = stopwatch.Elapsed;
        }
        recovery.Should().Be(new MonitoringDispatchResult(EndpointCount, EndpointCount));
        queue.Jobs.Select(item => item.DurableWorkId).Distinct().Should().HaveCount(EndpointCount);
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await database.LogicalChecks.CountAsync()).Should().Be(EndpointCount);
            (await database.DurableWork.CountAsync()).Should().Be(EndpointCount);
            (await database.DurableWork.CountAsync(item => item.State == "Enqueued")).Should().Be(EndpointCount);
        }
        await File.AppendAllTextAsync(evidencePath, $$"""


            ## PostgreSQL outage recovery

            The disposable PostgreSQL cluster was stopped after the load windows, connection refusal was confirmed, and the same data directory was restarted. Reconciliation then claimed and acknowledged {{recovery.ClaimedCount}} / {{recovery.EnqueuedCount}} outstanding items in {{duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}} ms. The database still contained exactly 500 logical checks and 500 durable-work rows, and the queue observed 500 unique durable-work IDs. No database repair was performed.
            """, new UTF8Encoding(false));
    }

    private static ServiceProvider BuildServices(string connectionString, TimeProvider clock, ControlledQueue queue)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString,
            ["BootstrapAdmin:Email"] = "representative-admin@example.test",
            ["BootstrapAdmin:DisplayName"] = "Representative Administrator",
            ["BootstrapAdmin:Password"] = $"Representative-9!{Guid.NewGuid():N}",
            ["Monitoring:Scheduling:Enabled"] = "true",
            ["Monitoring:Scheduling:DispatchBatchSize"] = "500",
            ["Monitoring:Scheduling:RecoveryBatchSize"] = "1000",
            ["Monitoring:Scheduling:RecoveryDelay"] = "00:01:00",
            ["Monitoring:HttpTransport:GlobalConcurrency"] = "100"
        }).Build();
        var collection = new ServiceCollection().AddLogging().AddSingleton(clock).AddSingleton<TimeProvider>(clock)
            .AddInfrastructure(configuration);
        collection.RemoveAll<ILogicalCheckQueue>();
        collection.AddSingleton<ILogicalCheckQueue>(queue);
        return collection.BuildServiceProvider();
    }

    private static async Task SeedFleetAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().BootstrapAsync();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var administrator = await database.Users.SingleAsync(item => item.Email == "representative-admin@example.test");
        var owner = await database.OwnerSubjects.Where(item => item.UserId == administrator.Id).Select(item => item.Id).SingleAsync();
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var clients = scope.ServiceProvider.GetRequiredService<IClientRegistryService>();
        var websites = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        var environments = scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>();
        var endpoints = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var permissions = scope.ServiceProvider.GetRequiredService<ITargetPermissionService>();
        var client = await clients.CreateAsync(new("Representative Fleet", owner, null), access);
        client.Succeeded.Should().BeTrue(string.Join(" ", client.Errors));
        var website = await websites.CreateAsync(new(client.EntityId!.Value, "Controlled Targets", owner, null, false, []), access);
        website.Succeeded.Should().BeTrue(string.Join(" ", website.Errors));
        var environment = await environments.CreateAsync(new(website.EntityId!.Value, "Representative",
            EnvironmentTypes.Production, null, true), access);
        environment.Succeeded.Should().BeTrue(string.Join(" ", environment.Errors));
        for (var index = 0; index < EndpointCount; index++)
        {
            var scheme = index < HttpsEndpointCount ? "https" : "http";
            var url = $"{scheme}://target-{index:D3}.representative.test/status";
            var httpReason = scheme == "http" ? "Controlled representative fixture requires a mixed scheme fleet." : null;
            var endpoint = await endpoints.CreateAsync(new(environment.EntityId!.Value, url, null, true, httpReason), access);
            endpoint.Succeeded.Should().BeTrue(string.Join(" ", endpoint.Errors));
            var permission = await permissions.GrantAsync(new(endpoint.EntityId!.Value, url, "Owned",
                "Controlled local representative fixture", null), access, CancellationToken.None);
            permission.Succeeded.Should().BeTrue(string.Join(" ", permission.Errors));
        }
        var enabled = await websites.UpdateAsync(new(website.EntityId.Value, "Controlled Targets", owner, null, true, 1, []), access);
        enabled.Succeeded.Should().BeTrue(string.Join(" ", enabled.Errors));
        database.ChangeTracker.Clear();
        var monitors = await database.EndpointMonitors.OrderBy(item => item.Id).ToArrayAsync();
        monitors.Should().HaveCount(ExpectedMonitorCount);
        foreach (var monitor in monitors) monitor.NextDueAt = AsOf.AddHours(1);
        var httpMonitors = monitors.Where(item => item.MonitorType == "HttpAvailability").OrderBy(item => item.Id).ToArray();
        httpMonitors.Should().HaveCount(EndpointCount);
        for (var index = 0; index < httpMonitors.Length; index++)
            httpMonitors[index].NextDueAt = AsOf.AddSeconds(-(index % 90));
        await database.SaveChangesAsync();
    }

    private static async Task<MemoryWindow> MeasureMemoryWindowAsync(SafeHttpConcurrencyLimiter limiter, TimeSpan duration)
    {
        var process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var samples = new List<long>();
        var operations = 0L;
        var maximumConcurrency = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            maximumConcurrency = Math.Max(maximumConcurrency, await ExerciseConcurrencyAsync(limiter, TimeSpan.FromMilliseconds(10)));
            operations += EndpointCount;
            process.Refresh();
            samples.Add(process.PrivateMemorySize64);
            var remaining = TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(10);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        }
        process.Refresh();
        return new(samples[0], process.PrivateMemorySize64, samples.Max(), operations, maximumConcurrency,
            samples.Zip(samples.Skip(1)).All(pair => pair.First < pair.Second));
    }

    private static async Task<int> ExerciseConcurrencyAsync(SafeHttpConcurrencyLimiter limiter, TimeSpan hold)
    {
        var active = 0;
        var maximum = 0;
        await Task.WhenAll(Enumerable.Range(0, EndpointCount).Select(async _ =>
        {
            using var lease = await limiter.AcquireGlobalAsync(CancellationToken.None);
            var current = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = maximum;
                if (current <= observed) break;
            } while (Interlocked.CompareExchange(ref maximum, current, observed) != observed);
            await Task.Delay(hold);
            Interlocked.Decrement(ref active);
        }));
        return maximum;
    }

    private static TimeSpan Percentile95(IReadOnlyList<TimeSpan> values) =>
        values[(int)Math.Ceiling(values.Count * 0.95) - 1];

    private static string RenderEvidence(string postgresql, MonitoringDispatchResult dispatch, TimeSpan dispatchDuration,
        ScheduleEvidence schedule, MonitoringDispatchResult recovery, TimeSpan recoveryDuration, ControlledQueue queue,
        MemoryWindow first, MemoryWindow second, int windowMinutes) => $$"""
        # Representative monitoring evidence

        Generated from controlled `.test` targets. No third-party target was contacted.

        | Property | Value |
        |---|---|
        | OS | {{RuntimeInformation.OSDescription}} |
        | Process architecture | {{RuntimeInformation.ProcessArchitecture}} |
        | Logical processors | {{Environment.ProcessorCount}} |
        | .NET runtime | {{RuntimeInformation.FrameworkDescription}} |
        | PostgreSQL | {{postgresql}} |
        | Execution model | One dispatcher, one reconciler, 100 concurrent request slots |
        | Endpoints | {{EndpointCount}} |
        | HTTPS endpoints | {{HttpsEndpointCount}} (70%) |
        | HTTP monitors | {{EndpointCount}} |
        | SSL monitors | {{HttpsEndpointCount}} |
        | Configured global concurrency | 100 |

        ## Scheduling and restart recovery

        | Measurement | Result |
        |---|---:|
        | Due monitors claimed | {{dispatch.ClaimedCount}} |
        | Initial queue acknowledgements | {{dispatch.EnqueuedCount}} |
        | Dispatch duration | {{dispatchDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}} ms |
        | Creation lag p95 | {{Percentile95(schedule.CreationLags).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}} s |
        | Creation lag maximum | {{schedule.CreationLags[^1].TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}} s |
        | Normal queue age p95 | {{Percentile95(schedule.QueueAges).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}} s |
        | Normal queue age maximum | {{schedule.QueueAges[^1].TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}} s |
        | Injected enqueue failures | 1 |
        | Restart recovery claimed / acknowledged | {{recovery.ClaimedCount}} / {{recovery.EnqueuedCount}} |
        | Recovery duration | {{recoveryDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}} ms |
        | Unique durable work IDs observed | {{queue.Jobs.Select(item => item.DurableWorkId).Distinct().Count()}} |
        | Duplicate logical checks or durable work rows | 0 |

        ## Memory windows

        Each window ran for {{windowMinutes}} minutes after warm-up and repeatedly admitted 500 operations through the configured 100-slot global limiter.

        | Window | Operations | Maximum concurrency | Start private bytes | End private bytes | Peak private bytes | Strictly monotonic |
        |---|---:|---:|---:|---:|---:|---|
        | 1 | {{first.Operations}} | {{first.MaximumConcurrency}} | {{first.StartBytes}} | {{first.EndBytes}} | {{first.PeakBytes}} | {{first.StrictlyMonotonic}} |
        | 2 | {{second.Operations}} | {{second.MaximumConcurrency}} | {{second.StartBytes}} | {{second.EndBytes}} | {{second.PeakBytes}} | {{second.StrictlyMonotonic}} |
        """;

    private sealed record ScheduleEvidence(TimeSpan[] CreationLags, TimeSpan[] QueueAges);
    private sealed record MemoryWindow(long StartBytes, long EndBytes, long PeakBytes, long Operations,
        int MaximumConcurrency, bool StrictlyMonotonic);

    private sealed class ControlledQueue : ILogicalCheckQueue
    {
        private readonly ConcurrentQueue<(Guid LogicalCheckId, Guid DurableWorkId)> jobs = new();
        private int calls;
        public int FailOnCall { get; init; }
        public IReadOnlyList<(Guid LogicalCheckId, Guid DurableWorkId)> Jobs => jobs.ToArray();

        public string Enqueue(Guid logicalCheckId, Guid durableWorkId)
        {
            if (Interlocked.Increment(ref calls) == FailOnCall) throw new InvalidOperationException("Controlled enqueue failure.");
            jobs.Enqueue((logicalCheckId, durableWorkId));
            return calls.ToString(CultureInfo.InvariantCulture);
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}

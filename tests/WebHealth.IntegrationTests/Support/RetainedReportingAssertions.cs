using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Reporting;

namespace WebHealth.IntegrationTests.Support;

internal static class RetainedReportingAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId)
    {
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var clock = new ReportingClock(now);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock)
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var serviceScope = services.CreateAsyncScope();
        var database = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint.Environment.Website).SingleAsync(item => item.Id == monitorId);
        var oldDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-120);
        var oldStart = new DateTimeOffset(oldDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var recentStart = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        async Task<Guid> AddResult(DateTimeOffset measured, int duration, string outcome, string? category)
        {
            var check = new LogicalCheck
            {
                Id = Guid.NewGuid(),
                EndpointMonitorId = monitorId,
                Source = "Scheduled",
                ScheduledFor = measured,
                CadenceKey = Guid.NewGuid().ToString(),
                State = "Completed",
                PolicyFingerprint = monitor.ConfigurationFingerprint,
                CreatedAt = measured,
                QueuedAt = measured,
                StartedAt = measured,
                CompletedAt = measured
            };
            database.LogicalChecks.Add(check);
            database.CheckConfigurationSnapshots.Add(CheckConfigurationSnapshotFactory.Create(monitor, check.Id, measured, NullLogger.Instance));
            database.CheckResults.Add(new CheckResult
            {
                LogicalCheckId = check.Id,
                EndpointMonitorId = monitorId,
                Outcome = outcome,
                FailureCategory = category,
                MonitorSource = "Scheduled",
                ConfigurationIdentity = MonitoringConfigurationIdentity.Format(
                    monitor.ConfigurationFingerprint, 2, monitor.CurrentTruthGeneration),
                CountsForUptime = true,
                TotalDurationMs = duration,
                MeasuredAt = measured,
                CompletedAt = measured
            });
            await database.SaveChangesAsync();
            return check.Id;
        }
        await AddResult(oldStart.AddHours(1), 100, "Healthy", null);
        var heldId = await AddResult(oldStart.AddHours(2), 300, "Warning", "SlowResponse");
        await AddResult(oldStart.AddHours(3), 500, "Critical", "ServerError");
        await AddResult(recentStart.AddHours(1), 50, "Healthy", null);
        ReportQuery Query(DateTimeOffset start, DateTimeOffset end)
        {
            var normalized = ReportQueryNormalizer.Normalize(new(WindowStart: start, WindowEnd: end), ReportMonitorTypes.All, now);
            normalized.Succeeded.Should().BeTrue(string.Join(" ", normalized.Errors));
            return normalized.Query!;
        }
        var query = Query(oldStart, recentStart.AddDays(1));
        var samples = new RetainedReportSamples(database);
        var writer = new DailyAggregateWriter(database, clock);
        (await writer.RecomputeAsync(monitorId, oldDay)).Should().BeTrue();
        var before = (await samples.LoadAsync(query, [monitorId], ReportSampleGrouping.Summary, CancellationToken.None))[string.Empty];
        before.ToHistory().Should().Be(new ReportHistoryCoverage(1, 3));
        before.ToResponseTimes().Should().Be(new ReportResponseTimes(100, 280, 3));
        database.RetentionHolds.Add(new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = "LogicalCheck",
            ScopeId = heldId,
            Reason = "Controlled reporting retention",
            CreatedByUserId = monitor.Endpoint.CreatedByUserId,
            CreatedAt = now
        });
        await database.SaveChangesAsync();
        var batch = new RawResultRetentionBatch(database, new() { Enabled = true, DryRun = false }, clock, writer,
            NullLogger<RawResultRetentionBatch>.Instance);
        (await batch.ExecuteAsync()).Should().Be(new RetentionBatchResult(2, 2));
        var combined = (await samples.LoadAsync(query, [monitorId], ReportSampleGrouping.Summary, CancellationToken.None))[string.Empty];
        combined.ToUptime().Should().Be(new ReportUptime(4, 2, 1, 1, 0));
        combined.ToHistory().Should().Be(new ReportHistoryCoverage(1, 3));
        combined.ToResponseTimes().Should().Be(new ReportResponseTimes(100, 300, 3, true));
        (await database.CheckResults.AnyAsync(item => item.LogicalCheckId == heldId)).Should().BeTrue();
        var trend = await samples.LoadAsync(query, [monitorId], ReportSampleGrouping.Day, CancellationToken.None);
        trend[oldDay.ToString("yyyy-MM-dd")].ToUptime().EligibleSamples.Should().Be(3);
        trend[oldDay.ToString("yyyy-MM-dd")].ToResponseTimes().IsApproximate.Should().BeTrue();
        trend[DateOnly.FromDateTime(recentStart.UtcDateTime).ToString("yyyy-MM-dd")].ToResponseTimes()
            .Should().Be(new ReportResponseTimes(50, 50, 1));
        var partial = (await samples.LoadAsync(Query(oldStart.AddMinutes(30), query.WindowEnd), [monitorId],
            ReportSampleGrouping.Summary, CancellationToken.None))[string.Empty];
        partial.ToHistory().Should().Be(new ReportHistoryCoverage(1, 0, true));
        partial.ToUptime().EligibleSamples.Should().Be(1);
        var fullYear = (await samples.LoadAsync(Query(recentStart.AddDays(-364), recentStart.AddDays(2)), [monitorId],
            ReportSampleGrouping.Summary, CancellationToken.None))[string.Empty];
        fullYear.ToUptime().Should().Be(combined.ToUptime());
        (await samples.AssessComparabilityAsync(query, [monitorId], CancellationToken.None)).IsComparable.Should().BeTrue();
        var reader = serviceScope.ServiceProvider.GetRequiredService<IReportingReader>();
        var access = new RegistryAccessContext(monitor.Endpoint.CreatedByUserId, [ApplicationRoles.Administrator]);
        var dataset = await reader.QueryAsync(query.WithPaging(1, ReportQueryNormalizer.MaximumMonitors), access);
        var row = dataset.Rows.Single(item => item.EndpointMonitorId == monitorId);
        row.Uptime.Should().Be(combined.ToUptime());
        row.ResponseTimes.Should().Be(combined.ToResponseTimes());
        row.History.Should().Be(combined.ToHistory());
        var fields = ReportingQueryCoreAssertions.ParseCsv(ReportCsv.Write(new(query, [row], 1))).Single();
        string Field(string name) => fields[Array.IndexOf(ReportCsv.Headers.ToArray(), name)];
        Field("HistoryMode").Should().Be("Mixed");
        Field("RawSamples").Should().Be("1");
        Field("AggregatedSamples").Should().Be("3");
        Field("PercentileMethod").Should().Be("ApproximateHistogram");
        monitor.CurrentTruthGeneration++;
        await database.SaveChangesAsync();
        await AddResult(recentStart.AddHours(2), 70, "Healthy", null);
        (await samples.AssessComparabilityAsync(query, [monitorId], CancellationToken.None)).ConfigurationChanged.Should().BeTrue();
        var logicalChecks = new LogicalCheckRetentionBatch(database, new() { Enabled = true, DryRun = false }, clock,
            NullLogger<LogicalCheckRetentionBatch>.Instance);
        (await logicalChecks.ExecuteAsync()).Should().Be(new RetentionBatchResult(2, 2));
    }

    private sealed class ReportingClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

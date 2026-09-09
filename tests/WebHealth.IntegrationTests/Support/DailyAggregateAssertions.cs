using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class DailyAggregateAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        var columns = await database.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name = 'monitoring_daily_aggregate'
            """).ToArrayAsync();
        columns.Should().BeEquivalentTo("endpoint_monitor_id", "utc_date", "total_count", "scheduled_count",
            "eligible_count", "healthy_count", "warning_count", "down_count", "maintenance_count", "cancelled_count",
            "excluded_count", "duration_count", "duration_sum_ms", "duration_minimum_ms", "duration_maximum_ms",
            "histogram_version", "duration_histogram", "exact_duration_samples", "comparability_identity", "is_comparable", "lowest_source",
            "highest_source", "first_measured_at", "last_measured_at", "computed_at", "raw_deletion_started_at");
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var row = NewRow(monitorId, now);
        database.MonitoringDailyAggregates.Add(row);
        await database.SaveChangesAsync();
        var saved = await database.MonitoringDailyAggregates.AsNoTracking().SingleAsync(item => item.EndpointMonitorId == monitorId);
        saved.Should().BeEquivalentTo(row);
        foreach (var mutation in new[]
        {
            "eligible_count = 2", "excluded_count = -1", "maintenance_count = 1", "duration_count = 2",
            "duration_sum_ms = 101", "duration_minimum_ms = NULL", "histogram_version = 2",
            "duration_histogram = ARRAY[1]::bigint[]", "duration_histogram[1] = -1",
            "duration_histogram[1] = NULL", "duration_histogram[1] = 1", "comparability_identity = 'invalid'",
            "exact_duration_samples = ARRAY[]::integer[]", "exact_duration_samples = ARRAY[-1]::integer[]",
            "lowest_source = ''", "last_measured_at = first_measured_at - interval '1 microsecond'",
            "utc_date = utc_date + 1", "raw_deletion_started_at = computed_at - interval '1 microsecond'"
        })
        {
            var statement = "UPDATE web_health.monitoring_daily_aggregate SET " + mutation + " WHERE endpoint_monitor_id = {0}";
            var invalid = async () => await database.Database.ExecuteSqlRawAsync(statement, monitorId);
            (await invalid.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
        var duplicate = async () => await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO web_health.monitoring_daily_aggregate
            SELECT * FROM web_health.monitoring_daily_aggregate WHERE endpoint_monitor_id = {monitorId}
            """);
        (await duplicate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        row.ExactDurationSamples = null;
        row.RawDeletionStartedAt = now;
        await database.SaveChangesAsync();
        database.MonitoringDailyAggregates.Remove(row);
        await database.SaveChangesAsync();
        await VerifyRetentionAsync(database, monitorId, now);
    }

    private static async Task VerifyRetentionAsync(ApplicationDbContext database, Guid monitorId, DateTimeOffset now)
    {
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint.Environment.Website).SingleAsync(item => item.Id == monitorId);
        var cutoff = DateOnly.FromDateTime(now.AddMonths(-24).UtcDateTime);
        var dates = new Dictionary<string, DateOnly>
        {
            ["eligible-a"] = cutoff.AddDays(-1),
            ["eligible-b"] = cutoff.AddDays(-2),
            ["raw"] = cutoff.AddDays(-3),
            ["unsealed"] = cutoff.AddDays(-4),
            ["boundary"] = cutoff
        };
        foreach (var pair in dates)
        {
            var measured = new DateTimeOffset(pair.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var aggregate = NewRow(monitorId, measured);
            aggregate.RawDeletionStartedAt = pair.Key == "unsealed" ? null : now;
            if (aggregate.RawDeletionStartedAt is not null) aggregate.ExactDurationSamples = null;
            database.MonitoringDailyAggregates.Add(aggregate);
        }
        var observed = new DateTimeOffset(dates["raw"].ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var check = new LogicalCheck
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitorId,
            Source = "Manual",
            RequestedAt = observed,
            InitiatedByUserId = monitor.Endpoint.CreatedByUserId,
            State = "Completed",
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = observed,
            QueuedAt = observed,
            StartedAt = observed,
            CompletedAt = observed
        };
        database.LogicalChecks.Add(check);
        database.CheckConfigurationSnapshots.Add(CheckConfigurationSnapshotFactory.Create(monitor, check.Id, observed, NullLogger.Instance));
        database.CheckResults.Add(new CheckResult
        {
            LogicalCheckId = check.Id,
            EndpointMonitorId = monitorId,
            Outcome = "Healthy",
            MonitorSource = "Manual",
            ConfigurationIdentity = MonitoringConfigurationIdentity.Format(
                monitor.ConfigurationFingerprint, 2, monitor.CurrentTruthGeneration),
            MeasuredAt = observed,
            CompletedAt = observed
        });
        await database.SaveChangesAsync();
        AggregateRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, new RetentionClock(now), NullLogger<AggregateRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.MonitoringDailyAggregates.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(5);
        foreach (var scope in new[] { "Monitor", "LogicalCheck" })
        {
            var hold = new RetentionHold
            {
                Id = Guid.NewGuid(),
                ScopeType = scope,
                ScopeId = scope == "Monitor" ? monitorId : check.Id,
                Reason = "Controlled aggregate retention",
                CreatedByUserId = monitor.Endpoint.CreatedByUserId,
                CreatedAt = now
            };
            database.RetentionHolds.Add(hold);
            await database.SaveChangesAsync();
            (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
            hold.ReleasedAt = now;
            hold.ReleasedByUserId = monitor.Endpoint.CreatedByUserId;
            await database.SaveChangesAsync();
        }
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await database.MonitoringDailyAggregates.Where(item => item.EndpointMonitorId == monitorId).Select(item => item.UtcDate).ToArrayAsync())
            .Should().BeEquivalentTo(new[] { dates["raw"], dates["unsealed"], dates["boundary"] });
        (await database.CheckResults.AnyAsync(item => item.LogicalCheckId == check.Id)).Should().BeTrue();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var execute = async () => await Batch(true, false).ExecuteAsync(cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class RetentionClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public static async Task VerifyUpgradeAsync(ApplicationDbContext database)
    {
        var monitorId = await database.EndpointMonitors.Where(item => item.MonitorType == "HttpAvailability"
            && item.Endpoint.NormalizedUrl == "https://ssl-fingerprint-upgrade.test/status")
            .Select(item => item.Id).SingleAsync();
        database.MonitoringDailyAggregates.Add(NewRow(monitorId, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        await database.Database.MigrateAsync("RetentionHolds");
        await database.Database.MigrateAsync();
        (await database.MonitoringDailyAggregates.CountAsync()).Should().Be(0);
        var row = NewRow(monitorId, DateTimeOffset.UtcNow);
        database.MonitoringDailyAggregates.Add(row);
        await database.SaveChangesAsync();
        await database.Database.MigrateAsync();
        (await database.MonitoringDailyAggregates.SingleAsync()).TotalCount.Should().Be(1);
        database.MonitoringDailyAggregates.Remove(row);
        await database.SaveChangesAsync();
    }

    public static MonitoringDailyAggregate NewRow(Guid monitorId, DateTimeOffset now) => new()
    {
        EndpointMonitorId = monitorId,
        UtcDate = DateOnly.FromDateTime(now.UtcDateTime),
        TotalCount = 1,
        ScheduledCount = 1,
        EligibleCount = 1,
        HealthyCount = 1,
        DurationCount = 1,
        DurationSumMs = 100,
        DurationMinimumMs = 100,
        DurationMaximumMs = 100,
        DurationHistogram = [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ExactDurationSamples = [100],
        ComparabilityIdentity = new string('a', 64),
        IsComparable = true,
        LowestSource = "WebHealthSafeHttpV1",
        HighestSource = "WebHealthSafeHttpV1",
        FirstMeasuredAt = now,
        LastMeasuredAt = now,
        ComputedAt = now
    };
}

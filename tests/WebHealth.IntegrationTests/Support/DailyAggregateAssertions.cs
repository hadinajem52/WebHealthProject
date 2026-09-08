using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
            "histogram_version", "duration_histogram", "comparability_identity", "is_comparable", "lowest_source",
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
            "lowest_source = 'Other'", "last_measured_at = first_measured_at - interval '1 microsecond'",
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
        row.RawDeletionStartedAt = now;
        await database.SaveChangesAsync();
        database.MonitoringDailyAggregates.Remove(row);
        await database.SaveChangesAsync();
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
        ComparabilityIdentity = new string('a', 64),
        IsComparable = true,
        LowestSource = "Scheduled",
        HighestSource = "Scheduled",
        FirstMeasuredAt = now,
        LastMeasuredAt = now,
        ComputedAt = now
    };
}

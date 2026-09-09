using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class MonitoringDailyAggregate
{
    public Guid EndpointMonitorId { get; set; }
    public DateOnly UtcDate { get; set; }
    public long TotalCount { get; set; }
    public long ScheduledCount { get; set; }
    public long EligibleCount { get; set; }
    public long HealthyCount { get; set; }
    public long WarningCount { get; set; }
    public long DownCount { get; set; }
    public long MaintenanceCount { get; set; }
    public long CancelledCount { get; set; }
    public long ExcludedCount { get; set; }
    public long DurationCount { get; set; }
    public long DurationSumMs { get; set; }
    public int? DurationMinimumMs { get; set; }
    public int? DurationMaximumMs { get; set; }
    public int HistogramVersion { get; set; } = ResponseTimeHistogram.Version;
    public long[] DurationHistogram { get; set; } = new long[ResponseTimeHistogram.BucketCount];
    public int[]? ExactDurationSamples { get; set; }
    public required string ComparabilityIdentity { get; set; }
    public bool IsComparable { get; set; }
    public required string LowestSource { get; set; }
    public required string HighestSource { get; set; }
    public DateTimeOffset FirstMeasuredAt { get; set; }
    public DateTimeOffset LastMeasuredAt { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public DateTimeOffset? RawDeletionStartedAt { get; set; }
}

internal sealed class MonitoringDailyAggregateConfiguration : IEntityTypeConfiguration<MonitoringDailyAggregate>
{
    public void Configure(EntityTypeBuilder<MonitoringDailyAggregate> builder)
    {
        builder.HasKey(item => new { item.EndpointMonitorId, item.UtcDate });
        builder.HasOne<EndpointMonitor>().WithMany().HasForeignKey(item => item.EndpointMonitorId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.ComparabilityIdentity).HasMaxLength(64);
        builder.Property(item => item.LowestSource).HasMaxLength(50);
        builder.Property(item => item.HighestSource).HasMaxLength(50);
        builder.ToTable("monitoring_daily_aggregate", table =>
        {
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_counts",
                "total_count > 0 AND scheduled_count BETWEEN 0 AND total_count AND eligible_count BETWEEN 0 AND scheduled_count "
                + "AND healthy_count >= 0 AND warning_count >= 0 AND down_count >= 0 "
                + "AND eligible_count::numeric = healthy_count::numeric + warning_count::numeric + down_count::numeric "
                + "AND excluded_count >= 0 AND total_count::numeric = eligible_count::numeric + excluded_count::numeric "
                + "AND maintenance_count BETWEEN 0 AND excluded_count AND cancelled_count BETWEEN 0 AND excluded_count");
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_duration",
                "duration_count BETWEEN 0 AND eligible_count AND duration_sum_ms >= 0 AND "
                + "((duration_count = 0 AND duration_sum_ms = 0 AND duration_minimum_ms IS NULL AND duration_maximum_ms IS NULL) OR "
                + "(duration_count > 0 AND duration_minimum_ms IS NOT NULL AND duration_maximum_ms IS NOT NULL "
                + "AND duration_minimum_ms >= 0 AND duration_maximum_ms >= duration_minimum_ms "
                + "AND duration_sum_ms::numeric BETWEEN duration_count::numeric * duration_minimum_ms AND duration_count::numeric * duration_maximum_ms))");
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_histogram",
                "histogram_version = 1 AND array_ndims(duration_histogram) = 1 AND array_lower(duration_histogram, 1) = 1 "
                + "AND cardinality(duration_histogram) = 12 AND array_position(duration_histogram, NULL) IS NULL "
                + "AND 0 <= ALL(duration_histogram) AND duration_count::numeric = "
                + string.Join(" + ", Enumerable.Range(1, 12).Select(index => $"duration_histogram[{index}]::numeric")));
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_identity", "comparability_identity ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_sources",
                "lowest_source <> '' AND highest_source <> '' AND lowest_source <= highest_source");
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_exact_samples",
                "exact_duration_samples IS NULL OR (raw_deletion_started_at IS NULL "
                + "AND array_ndims(exact_duration_samples) = 1 "
                + "AND array_lower(exact_duration_samples, 1) = 1 AND array_position(exact_duration_samples, NULL) IS NULL "
                + "AND 0 <= ALL(exact_duration_samples) AND cardinality(exact_duration_samples) = duration_count)");
            table.HasCheckConstraint("ck_monitoring_daily_aggregate_dates",
                "first_measured_at <= last_measured_at AND (first_measured_at AT TIME ZONE 'UTC')::date = utc_date "
                + "AND (last_measured_at AT TIME ZONE 'UTC')::date = utc_date "
                + "AND (raw_deletion_started_at IS NULL OR raw_deletion_started_at >= computed_at)");
        });
    }
}

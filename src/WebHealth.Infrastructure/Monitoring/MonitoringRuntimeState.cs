using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class MonitoringRuntimeState
{
    public required string Operation { get; set; }
    public Guid InvocationId { get; set; }
    public DateTimeOffset LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public long? LastDurationMs { get; set; }
    public string? FailureCategory { get; set; }
    public int ConsecutiveFailures { get; set; }
}

internal sealed class MonitoringRuntimeStateConfiguration : IEntityTypeConfiguration<MonitoringRuntimeState>
{
    public void Configure(EntityTypeBuilder<MonitoringRuntimeState> builder)
    {
        builder.HasKey(state => state.Operation);
        builder.Property(state => state.Operation).HasMaxLength(40);
        builder.Property(state => state.FailureCategory).HasMaxLength(40);
        builder.ToTable("monitoring_runtime_state", table =>
        {
            table.HasCheckConstraint("ck_monitoring_runtime_state_operation",
                "operation IN ('monitoring-dispatch', 'monitoring-reconciliation')");
            table.HasCheckConstraint("ck_monitoring_runtime_state_failures", "consecutive_failures >= 0");
            table.HasCheckConstraint("ck_monitoring_runtime_state_duration", "last_duration_ms IS NULL OR last_duration_ms >= 0");
            table.HasCheckConstraint("ck_monitoring_runtime_state_failure_category",
                "failure_category IS NULL OR failure_category IN ('Database', 'Cancellation', 'QueueEnqueue', 'Unexpected')");
        });
    }
}

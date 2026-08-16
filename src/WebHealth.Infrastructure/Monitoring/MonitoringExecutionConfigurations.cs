using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class LogicalCheckConfiguration : IEntityTypeConfiguration<LogicalCheck>
{
    public void Configure(EntityTypeBuilder<LogicalCheck> builder)
    {
        builder.ToTable("logical_check", table =>
        {
            table.HasCheckConstraint(
                "ck_logical_check_source",
                "source IN ('Scheduled', 'Manual', 'Urgent')");
            table.HasCheckConstraint(
                "ck_logical_check_state",
                "state IN ('Pending', 'Queued', 'Running', 'Completed')");
            table.HasCheckConstraint(
                "ck_logical_check_source_fields",
                "(source = 'Scheduled' AND scheduled_for IS NOT NULL AND cadence_key IS NOT NULL "
                + "AND requested_at IS NULL AND initiated_by_user_id IS NULL) OR "
                + "(source = 'Manual' AND scheduled_for IS NULL AND cadence_key IS NULL "
                + "AND requested_at IS NOT NULL AND initiated_by_user_id IS NOT NULL) OR "
                + "(source = 'Urgent' AND scheduled_for IS NULL AND cadence_key IS NULL "
                + "AND requested_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_logical_check_timestamps",
                "(queued_at IS NULL OR queued_at >= created_at) "
                + "AND (started_at IS NULL OR (queued_at IS NOT NULL AND started_at >= queued_at)) "
                + "AND (completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at))");
        });
        builder.Property(check => check.Source).HasMaxLength(20).IsRequired();
        builder.Property(check => check.State).HasMaxLength(20).IsRequired();
        builder.Property(check => check.CadenceKey).HasMaxLength(100);
        builder.Property(check => check.PolicyFingerprint).HasMaxLength(64).IsRequired();
        builder.ToTable("logical_check", table => table.HasCheckConstraint(
            "ck_logical_check_policy_fingerprint",
            "length(policy_fingerprint) = 64"));
        builder.HasAlternateKey(check => new { check.Id, check.EndpointMonitorId })
            .HasName("ak_logical_check_id_endpoint_monitor_id");
        builder.HasIndex(check => new { check.EndpointMonitorId, check.CadenceKey })
            .IsUnique().HasFilter("source = 'Scheduled'");
        builder.HasIndex(check => new { check.EndpointMonitorId, check.CreatedAt });
        builder.HasIndex(check => new { check.State, check.CreatedAt });
        builder.HasOne(check => check.EndpointMonitor).WithMany(monitor => monitor.LogicalChecks)
            .HasForeignKey(check => check.EndpointMonitorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(check => check.InitiatedByUser).WithMany()
            .HasForeignKey(check => check.InitiatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CheckConfigurationSnapshotConfiguration
    : IEntityTypeConfiguration<CheckConfigurationSnapshot>
{
    public void Configure(EntityTypeBuilder<CheckConfigurationSnapshot> builder)
    {
        builder.ToTable("check_configuration_snapshot", table =>
        {
            table.HasCheckConstraint("ck_check_configuration_snapshot_schema_version", "schema_version > 0");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_positive_values",
                "interval_seconds > 0 AND timeout_seconds > 0 "
                + "AND failure_confirmation_count > 0 AND recovery_confirmation_count > 0");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_threshold_order",
                "(warning_threshold_ms IS NULL OR warning_threshold_ms >= 0) "
                + "AND (critical_threshold_ms IS NULL OR critical_threshold_ms >= 0) "
                + "AND (warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL "
                + "OR warning_threshold_ms < critical_threshold_ms)");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_sources",
                "interval_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride') "
                + "AND timeout_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride') "
                + "AND confirmation_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride') "
                + "AND threshold_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride')");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_fingerprint",
                "length(configuration_fingerprint) = 64");
        });
        builder.HasKey(snapshot => snapshot.LogicalCheckId);
        builder.Property(snapshot => snapshot.MonitorType).HasMaxLength(50).IsRequired();
        builder.Property(snapshot => snapshot.ConfigurationFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.IntervalSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.TimeoutSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.ConfirmationSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.ThresholdSource).HasMaxLength(30).IsRequired();
        builder.HasOne(snapshot => snapshot.LogicalCheck).WithOne(check => check.ConfigurationSnapshot)
            .HasForeignKey<CheckConfigurationSnapshot>(snapshot => snapshot.LogicalCheckId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExecutionAttemptConfiguration : IEntityTypeConfiguration<ExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<ExecutionAttempt> builder)
    {
        builder.ToTable("execution_attempt", table =>
        {
            table.HasCheckConstraint("ck_execution_attempt_number", "attempt_number > 0");
            table.HasCheckConstraint(
                "ck_execution_attempt_outcome",
                "infrastructure_outcome IN ('Running', 'Succeeded', 'RetryableFailure', 'TerminalFailure', 'Cancelled')");
            table.HasCheckConstraint(
                "ck_execution_attempt_finished",
                "finished_at IS NULL OR finished_at >= started_at");
        });
        builder.Property(attempt => attempt.JobId).HasMaxLength(100).IsRequired();
        builder.Property(attempt => attempt.WorkerId).HasMaxLength(100).IsRequired();
        builder.Property(attempt => attempt.InfrastructureOutcome).HasMaxLength(30).IsRequired();
        builder.Property(attempt => attempt.FailureCategory).HasMaxLength(100);
        builder.HasIndex(attempt => new { attempt.LogicalCheckId, attempt.AttemptNumber }).IsUnique();
        builder.HasOne(attempt => attempt.LogicalCheck).WithMany(check => check.Attempts)
            .HasForeignKey(attempt => attempt.LogicalCheckId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExecutionLeaseConfiguration : IEntityTypeConfiguration<ExecutionLease>
{
    public void Configure(EntityTypeBuilder<ExecutionLease> builder)
    {
        builder.ToTable("execution_lease", table =>
        {
            table.HasCheckConstraint("ck_execution_lease_generation", "fencing_generation > 0");
            table.HasCheckConstraint("ck_execution_lease_expiry", "expires_at > acquired_at");
        });
        builder.HasKey(lease => lease.EndpointMonitorId);
        builder.HasIndex(lease => lease.ExpiresAt);
        builder.HasOne(lease => lease.EndpointMonitor).WithOne(monitor => monitor.ExecutionLease)
            .HasForeignKey<ExecutionLease>(lease => lease.EndpointMonitorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(lease => lease.LogicalCheck).WithMany()
            .HasForeignKey(lease => new { lease.LogicalCheckId, lease.EndpointMonitorId })
            .HasPrincipalKey(check => new { check.Id, check.EndpointMonitorId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_execution_lease_logical_check_monitor");
    }
}

internal sealed class DurableWorkConfiguration : IEntityTypeConfiguration<DurableWork>
{
    public void Configure(EntityTypeBuilder<DurableWork> builder)
    {
        builder.ToTable("durable_work", table =>
        {
            table.HasCheckConstraint(
                "ck_durable_work_state",
                "state IN ('Pending', 'Enqueued', 'Processing', 'Completed', 'Failed')");
            table.HasCheckConstraint("ck_durable_work_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_durable_work_lease_fields",
                "(lease_owner_token IS NULL AND lease_acquired_at IS NULL AND lease_expires_at IS NULL) OR "
                + "(lease_owner_token IS NOT NULL AND lease_acquired_at IS NOT NULL "
                + "AND lease_expires_at IS NOT NULL AND lease_expires_at > lease_acquired_at)");
            table.HasCheckConstraint("ck_durable_work_updated", "updated_at >= created_at");
        });
        builder.Property(work => work.WorkKind).HasMaxLength(50).IsRequired();
        builder.Property(work => work.DedupeKey).HasMaxLength(200).IsRequired();
        builder.Property(work => work.QueueName).HasMaxLength(50).IsRequired();
        builder.Property(work => work.State).HasMaxLength(30).IsRequired();
        builder.Property(work => work.LastFailureCategory).HasMaxLength(100);
        builder.HasIndex(work => new { work.WorkKind, work.DedupeKey }).IsUnique();
        builder.HasIndex(work => new { work.State, work.AvailableAt });
        builder.HasIndex(work => work.LogicalCheckId);
        builder.HasOne(work => work.LogicalCheck).WithMany(check => check.DurableWork)
            .HasForeignKey(work => work.LogicalCheckId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Maintenance;

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
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_marker_comparison",
                "content_marker_comparison IN ('Ordinal', 'OrdinalIgnoreCase')");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_http_severity",
                "production_http_severity IN ('Warning', 'Critical')");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_http_limits",
                "max_response_body_bytes BETWEEN 1 AND 2097152 AND max_redirects BETWEEN 0 AND 10");
            table.HasCheckConstraint(
                "ck_check_configuration_snapshot_accepted_statuses",
                "accepted_status_codes = '' OR accepted_status_codes ~ "
                + "'^[1-5][0-9]{2}(,[1-5][0-9]{2})*$'");
        });
        builder.HasKey(snapshot => snapshot.LogicalCheckId);
        builder.Property(snapshot => snapshot.MonitorType).HasMaxLength(50).IsRequired();
        builder.Property(snapshot => snapshot.ConfigurationFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.IntervalSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.TimeoutSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.ConfirmationSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.ThresholdSource).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.AcceptedStatusCodes).HasMaxLength(500).IsRequired();
        builder.Property(snapshot => snapshot.RequiredContentMarker).HasMaxLength(500);
        builder.Property(snapshot => snapshot.ContentMarkerComparison).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.ProductionHttpSeverity).HasMaxLength(20).IsRequired();
        builder.HasOne(snapshot => snapshot.LogicalCheck).WithOne(check => check.ConfigurationSnapshot)
            .HasForeignKey<CheckConfigurationSnapshot>(snapshot => snapshot.LogicalCheckId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CertificateObservationConfiguration
    : IEntityTypeConfiguration<CertificateObservation>
{
    public void Configure(EntityTypeBuilder<CertificateObservation> builder)
    {
        builder.ToTable("certificate_observation", table =>
        {
            table.HasCheckConstraint(
                "ck_certificate_observation_validity_window",
                "not_after >= not_before");
            table.HasCheckConstraint(
                "ck_certificate_observation_fingerprint",
                "sha256_fingerprint ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_certificate_observation_category",
                "validation_category IN ('Valid', 'NotYetValid', 'Expired', 'HostnameMismatch', 'Untrusted')");
        });
        builder.HasKey(observation => observation.LogicalCheckId);
        builder.Property(observation => observation.Subject).HasMaxLength(512).IsRequired();
        builder.Property(observation => observation.Issuer).HasMaxLength(512).IsRequired();
        builder.Property(observation => observation.SerialNumber).HasMaxLength(128).IsRequired();
        builder.Property(observation => observation.Sha256Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(observation => observation.ValidationCategory).HasMaxLength(30).IsRequired();
        builder.Property(observation => observation.SubjectAlternativeNames).HasMaxLength(1024);
        builder.HasIndex(observation => new { observation.EndpointMonitorId, observation.ObservedAt })
            .IsDescending(false, true);
        builder.HasIndex(observation => observation.Sha256Fingerprint);
        // The composite key stops an observation from claiming a logical check that belongs to
        // one monitor while pointing at another; the application writes matching values, and
        // this makes the database refuse anything else.
        builder.HasOne(observation => observation.LogicalCheck).WithOne()
            .HasForeignKey<CertificateObservation>(observation =>
                new { observation.LogicalCheckId, observation.EndpointMonitorId })
            .HasPrincipalKey<LogicalCheck>(check => new { check.Id, check.EndpointMonitorId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(observation => observation.EndpointMonitor).WithMany()
            .HasForeignKey(observation => observation.EndpointMonitorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CheckResultConfiguration : IEntityTypeConfiguration<CheckResult>
{
    public void Configure(EntityTypeBuilder<CheckResult> builder)
    {
        builder.ToTable("check_result", table =>
        {
            table.HasCheckConstraint(
                "ck_check_result_outcome",
                "outcome IN ('Healthy', 'Warning', 'Critical', 'Cancelled')");
            table.HasCheckConstraint(
                "ck_check_result_http_status",
                "http_status IS NULL OR http_status BETWEEN 100 AND 599");
            table.HasCheckConstraint(
                "ck_check_result_timings",
                "dns_duration_ms IS NULL OR dns_duration_ms >= 0");
            table.HasCheckConstraint(
                "ck_check_result_more_timings",
                "(connect_duration_ms IS NULL OR connect_duration_ms >= 0) "
                + "AND (tls_duration_ms IS NULL OR tls_duration_ms >= 0) "
                + "AND (ttfb_duration_ms IS NULL OR ttfb_duration_ms >= 0) "
                + "AND total_duration_ms >= 0");
            table.HasCheckConstraint(
                "ck_check_result_lengths",
                "(transferred_length IS NULL OR transferred_length >= 0) "
                + "AND (decoded_length IS NULL OR decoded_length >= 0) "
                + "AND ((decoded_length IS NULL AND length_source IS NULL) "
                + "OR (decoded_length IS NOT NULL AND length_source IS NOT NULL))");
            table.HasCheckConstraint("ck_check_result_completed", "completed_at >= measured_at");
            table.HasCheckConstraint(
                "ck_check_result_failure_category",
                "failure_category IS NULL OR failure_category IN "
                + "('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError',"
                + "'RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge',"
                + "'HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect',"
                + "'ExecutionExhausted','TargetIneligible','Protocol',"
                + "'SlowResponse','PageTooLarge',"
                + "'SslExpired','SslNotYetValid','SslHostnameMismatch','SslUntrusted',"
                + "'SslHandshakeFailed','SslExpiringSoon')");
            table.HasCheckConstraint(
                "ck_check_result_outcome_category",
                "(outcome = 'Healthy' AND failure_category IS NULL) OR "
                + "(outcome = 'Cancelled' AND failure_category IN ('Cancellation','TargetIneligible')) OR "
                + "(outcome IN ('Warning','Critical') AND failure_category IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_check_result_truncation",
                "NOT response_truncated OR failure_category = 'ResponseTooLarge'");
            table.HasCheckConstraint(
                "ck_check_result_maintenance",
                "(is_maintenance AND maintenance_occurrence_id IS NOT NULL) OR "
                + "(NOT is_maintenance AND maintenance_occurrence_id IS NULL)");
        });
        builder.HasKey(result => result.LogicalCheckId);
        builder.Property(result => result.Outcome).HasMaxLength(20).IsRequired();
        builder.Property(result => result.FailureCategory).HasMaxLength(50);
        builder.Property(result => result.LengthSource).HasMaxLength(30);
        builder.Property(result => result.MonitorSource).HasMaxLength(50).IsRequired();
        builder.Property(result => result.SafeDiagnostic).HasMaxLength(200);
        builder.HasIndex(result => new { result.MeasuredAt, result.LogicalCheckId });
        builder.HasIndex(result => result.MaintenanceOccurrenceId);
        // The reporting index: every aggregate asks for one set of monitors over one window, so
        // both halves of the predicate are leading columns here. The payload columns are included
        // so the index carries everything the aggregates read, which lets PostgreSQL choose an
        // index-only scan when the visibility map permits one; the captured plans show it
        // choosing a bitmap heap scan over this index on a freshly loaded table, which is the
        // normal outcome before the pages are all-visible.
        builder.HasIndex(result => new { result.EndpointMonitorId, result.MeasuredAt })
            .IncludeProperties(result => new
            {
                result.Outcome,
                result.CountsForUptime,
                result.TotalDurationMs,
                result.MonitorSource
            })
            .HasDatabaseName("ix_check_result_monitor_measured_at");
        builder.HasOne(result => result.LogicalCheck).WithOne(check => check.Result)
            .HasForeignKey<CheckResult>(result => new { result.LogicalCheckId, result.EndpointMonitorId })
            .HasPrincipalKey<LogicalCheck>(check => new { check.Id, check.EndpointMonitorId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_check_result_logical_check_monitor");
        builder.HasOne(result => result.MaintenanceOccurrence).WithMany()
            .HasForeignKey(result => result.MaintenanceOccurrenceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_check_result_maintenance_occurrence_id");
    }
}

internal sealed class RedirectHopConfiguration : IEntityTypeConfiguration<RedirectHop>
{
    public void Configure(EntityTypeBuilder<RedirectHop> builder)
    {
        builder.ToTable("redirect_hop", table =>
        {
            table.HasCheckConstraint("ck_redirect_hop_number", "hop_number > 0");
            table.HasCheckConstraint("ck_redirect_hop_status", "http_status BETWEEN 300 AND 399");
        });
        builder.Property(hop => hop.NormalizedFromUrl).HasMaxLength(2048).IsRequired();
        builder.Property(hop => hop.NormalizedToUrl).HasMaxLength(2048).IsRequired();
        builder.HasIndex(hop => new { hop.LogicalCheckId, hop.HopNumber }).IsUnique();
        builder.HasOne(hop => hop.Result).WithMany(result => result.RedirectHops)
            .HasForeignKey(hop => hop.LogicalCheckId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("finding", table => table.HasCheckConstraint(
            "ck_finding_severity",
            "severity IN ('Warning', 'High', 'Critical')"));
        builder.Property(finding => finding.RuleKey).HasMaxLength(100).IsRequired();
        builder.Property(finding => finding.Severity).HasMaxLength(20).IsRequired();
        builder.Property(finding => finding.ObservedValue).HasMaxLength(500);
        builder.Property(finding => finding.ExpectedValue).HasMaxLength(500);
        builder.Property(finding => finding.IssueKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(finding => new
        {
            finding.LogicalCheckId,
            finding.IssueKey,
            finding.RuleKey
        }).IsUnique();
        builder.HasOne(finding => finding.Result).WithMany(result => result.Findings)
            .HasForeignKey(finding => finding.LogicalCheckId).OnDelete(DeleteBehavior.Restrict);
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
                "infrastructure_outcome IN ('Running', 'Succeeded', 'RetryableFailure', 'TerminalFailure', 'Cancelled', 'Superseded')");
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
                "state IN ('Pending', 'Dispatching', 'Enqueued', 'Processing', 'Completed', 'Failed')");
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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

public static class PageAuditTextBounds
{
    public const int Url = 2048;
    public const int Provider = 40;
    public const int Category = 20;
    public const int Strategy = 20;
    public const int Locale = 20;
    public const int Status = 30;
    public const int FailureCategory = 40;
    public const int LighthouseVersion = 40;
    public const int SafeDiagnostic = 1000;
    public const int WarningSummary = 2000;

    public const int AuditId = 200;
    public const int ScoreDisplayMode = 40;
    public const int NumericUnit = 30;
    public const int GroupName = 100;
    public const int Title = 500;
    public const int Description = 2000;
    public const int DisplayValue = 1000;
    public const int Explanation = 2000;
    public const int ErrorMessage = 1000;
}

internal sealed class PageAuditTargetConfiguration : IEntityTypeConfiguration<PageAuditTarget>
{
    public const int MinimumIntervalSeconds = 6 * 60 * 60;

    public const int MaximumIntervalSeconds = 30 * 24 * 60 * 60;

    public void Configure(EntityTypeBuilder<PageAuditTarget> builder)
    {
        builder.ToTable("page_audit_target", table =>
        {
            table.HasCheckConstraint(
                "ck_page_audit_target_provider",
                "provider IN ('PageSpeedInsights')");

            table.HasCheckConstraint(
                "ck_page_audit_target_category",
                "category IN ('Performance', 'Accessibility', 'BestPractices', 'Seo')");

            table.HasCheckConstraint(
                "ck_page_audit_target_strategy",
                "strategy IN ('Mobile', 'Desktop')");

            table.HasCheckConstraint(
                "ck_page_audit_target_interval",
                $"interval_seconds BETWEEN {MinimumIntervalSeconds} AND {MaximumIntervalSeconds}");

            table.HasCheckConstraint(
                "ck_page_audit_target_scheduling_requires_enabled",
                "is_enabled OR NOT scheduling_enabled");

            table.HasCheckConstraint(
                "ck_page_audit_target_updated_after_created",
                "updated_at >= created_at");
        });

        builder.HasKey(target => target.Id);
        builder.Property(target => target.Provider).HasMaxLength(PageAuditTextBounds.Provider).IsRequired();
        builder.Property(target => target.Category).HasMaxLength(PageAuditTextBounds.Category).IsRequired();
        builder.Property(target => target.Strategy).HasMaxLength(PageAuditTextBounds.Strategy).IsRequired();
        builder.Property(target => target.Version).IsConcurrencyToken();

        builder.HasIndex(target => new
        {
            target.EndpointId,
            target.Provider,
            target.Category,
            target.Strategy
        }).IsUnique().HasDatabaseName("ux_page_audit_target_profile");

        builder.HasIndex(target => new { target.NextDueAt, target.Id })
            .HasFilter("is_enabled AND scheduling_enabled")
            .HasDatabaseName("ix_page_audit_target_due");

        builder.HasOne(target => target.Endpoint).WithMany()
            .HasForeignKey(target => target.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PageAuditRunConfiguration : IEntityTypeConfiguration<PageAuditRun>
{
    public void Configure(EntityTypeBuilder<PageAuditRun> builder)
    {
        builder.ToTable("page_audit_run", table =>
        {
            table.HasCheckConstraint(
                "ck_page_audit_run_status",
                "status IN ('Queued', 'Running', 'Completed', 'CompletedWithWarnings', "
                + "'Failed', 'Cancelled')");

            table.HasCheckConstraint(
                "ck_page_audit_run_source",
                "source IN ('Scheduled', 'Manual')");

            table.HasCheckConstraint(
                "ck_page_audit_run_provider",
                "provider IN ('PageSpeedInsights')");

            table.HasCheckConstraint(
                "ck_page_audit_run_category",
                "category IN ('Performance', 'Accessibility', 'BestPractices', 'Seo')");

            table.HasCheckConstraint(
                "ck_page_audit_run_strategy",
                "strategy IN ('Mobile', 'Desktop')");

            table.HasCheckConstraint(
                "ck_page_audit_run_raw_score",
                "raw_score IS NULL OR raw_score BETWEEN 0 AND 1");

            table.HasCheckConstraint("ck_page_audit_run_attempt_count", "attempt_count >= 0");

            table.HasCheckConstraint(
                "ck_page_audit_run_finished_when_terminal",
                "(status IN ('Queued', 'Running')) = (finished_at IS NULL)");

            table.HasCheckConstraint(
                "ck_page_audit_run_finished_after_queued",
                "finished_at IS NULL OR finished_at >= queued_at");

            table.HasCheckConstraint(
                "ck_page_audit_run_completed_contract",
                "status NOT IN ('Completed', 'CompletedWithWarnings') "
                + "OR (raw_score IS NOT NULL AND failure_category IS NULL "
                + "AND lighthouse_version IS NOT NULL)");

            table.HasCheckConstraint(
                "ck_page_audit_run_failure_contract",
                "status <> 'Failed' OR failure_category IS NOT NULL");

            table.HasCheckConstraint(
                "ck_page_audit_run_failure_category",
                "failure_category IS NULL OR failure_category IN ("
                + "'ProviderRateLimited', 'ProviderUnavailable', 'ProviderTimeout', "
                + "'ProviderAuthenticationFailed', 'TargetRejected', 'CaptchaBlocked', "
                + "'LighthouseRuntimeError', 'ProviderContractInvalid', "
                + "'ProviderResponseTooLarge', 'ProviderResponseInvalid', 'Cancelled', "
                + "'UnknownProviderFailure')");

            table.HasCheckConstraint(
                "ck_page_audit_run_lease_pair",
                "(lease_token IS NULL) = (lease_expires_at IS NULL)");

            table.HasCheckConstraint(
                "ck_page_audit_run_terminal_has_no_lease",
                "status IN ('Queued', 'Running') OR lease_token IS NULL");
        });

        builder.HasKey(run => run.Id);
        builder.Property(run => run.BatchId).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(run => run.Source).HasMaxLength(PageAuditTextBounds.Status).IsRequired();
        builder.Property(run => run.Status).HasMaxLength(PageAuditTextBounds.Status).IsRequired();
        builder.Property(run => run.RequestedUrl).HasMaxLength(PageAuditTextBounds.Url).IsRequired();
        builder.Property(run => run.FinalUrl).HasMaxLength(PageAuditTextBounds.Url);
        builder.Property(run => run.Provider).HasMaxLength(PageAuditTextBounds.Provider).IsRequired();
        builder.Property(run => run.Category).HasMaxLength(PageAuditTextBounds.Category).IsRequired();
        builder.Property(run => run.Strategy).HasMaxLength(PageAuditTextBounds.Strategy).IsRequired();
        builder.Property(run => run.Locale).HasMaxLength(PageAuditTextBounds.Locale).IsRequired();
        builder.Property(run => run.LighthouseVersion).HasMaxLength(PageAuditTextBounds.LighthouseVersion);
        builder.Property(run => run.WarningSummary).HasMaxLength(PageAuditTextBounds.WarningSummary);
        builder.Property(run => run.FailureCategory).HasMaxLength(PageAuditTextBounds.FailureCategory);
        builder.Property(run => run.SafeDiagnostic).HasMaxLength(PageAuditTextBounds.SafeDiagnostic);

        builder.Property(run => run.RawScore).HasPrecision(5, 4);

        builder.HasIndex(run => new { run.PageAuditTargetId, run.FinishedAt, run.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_page_audit_run_target_finished");

        builder.HasIndex(run => new { run.EndpointId, run.FinishedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_page_audit_run_endpoint_finished");

        builder.HasIndex(run => new { run.Status, run.UpdatedAt })
            .HasDatabaseName("ix_page_audit_run_status_updated");

        builder.HasIndex(run => new { run.BatchId, run.Strategy })
            .HasDatabaseName("ix_page_audit_run_batch_strategy");

        builder.HasIndex(run => run.PageAuditTargetId)
            .IsUnique()
            .HasFilter("status IN ('Queued', 'Running')")
            .HasDatabaseName("ux_page_audit_run_active");

        builder.HasOne(run => run.Target).WithMany(target => target.Runs)
            .HasForeignKey(run => run.PageAuditTargetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PageAuditItemConfiguration : IEntityTypeConfiguration<PageAuditItem>
{
    public void Configure(EntityTypeBuilder<PageAuditItem> builder)
    {
        builder.ToTable("page_audit_item", table =>
        {
            table.HasCheckConstraint(
                "ck_page_audit_item_status",
                "status IN ('Passed', 'Failed', 'Scored', 'Manual', 'NotApplicable', "
                + "'Informative', 'Error')");

            table.HasCheckConstraint(
                "ck_page_audit_item_score",
                "score IS NULL OR score BETWEEN 0 AND 1");

            table.HasCheckConstraint("ck_page_audit_item_weight", "weight >= 0");

            table.HasCheckConstraint(
                "ck_page_audit_item_numeric_value",
                "numeric_value IS NULL OR numeric_value >= 0");

            table.HasCheckConstraint(
                "ck_page_audit_item_scored_statuses_have_a_score",
                "status NOT IN ('Passed', 'Failed', 'Scored') OR score IS NOT NULL");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.AuditId).HasMaxLength(PageAuditTextBounds.AuditId).IsRequired();
        builder.Property(item => item.Status).HasMaxLength(PageAuditTextBounds.Status).IsRequired();
        builder.Property(item => item.ScoreDisplayMode).HasMaxLength(PageAuditTextBounds.ScoreDisplayMode);
        builder.Property(item => item.NumericValue).HasPrecision(14, 4);
        builder.Property(item => item.NumericUnit).HasMaxLength(PageAuditTextBounds.NumericUnit);
        builder.Property(item => item.GroupName).HasMaxLength(PageAuditTextBounds.GroupName);
        builder.Property(item => item.Title).HasMaxLength(PageAuditTextBounds.Title);
        builder.Property(item => item.Description).HasMaxLength(PageAuditTextBounds.Description);
        builder.Property(item => item.DisplayValue).HasMaxLength(PageAuditTextBounds.DisplayValue);
        builder.Property(item => item.Explanation).HasMaxLength(PageAuditTextBounds.Explanation);
        builder.Property(item => item.ErrorMessage).HasMaxLength(PageAuditTextBounds.ErrorMessage);
        builder.Property(item => item.Score).HasPrecision(5, 4);

        builder.HasIndex(item => new { item.RunId, item.AuditId })
            .IsUnique()
            .HasDatabaseName("ux_page_audit_item_run_audit");

        builder.HasIndex(item => new { item.RunId, item.Status, item.AuditId })
            .HasDatabaseName("ix_page_audit_item_run_status");

        builder.HasOne(item => item.Run).WithMany(run => run.Items)
            .HasForeignKey(item => item.RunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class PageAuditIncidentPolicyDefaults
{
    public static PageAuditIncidentPolicyEntity Create(Guid endpointId, DateTimeOffset now) => new()
    {
        EndpointId = endpointId,
        IncidentsEnabled = false,
        PerformanceScoreEnabled = true,
        PerformanceMinimumScore = 90,
        AccessibilityScoreEnabled = true,
        AccessibilityMinimumScore = 90,
        BestPracticesScoreEnabled = true,
        BestPracticesMinimumScore = 90,
        SeoScoreEnabled = true,
        SeoMinimumScore = 90,
        FirstContentfulPaintEnabled = false,
        FirstContentfulPaintMaximum = 1800,
        LargestContentfulPaintEnabled = false,
        LargestContentfulPaintMaximum = 2500,
        TotalBlockingTimeEnabled = false,
        TotalBlockingTimeMaximum = 200,
        CumulativeLayoutShiftEnabled = false,
        CumulativeLayoutShiftMaximum = 0.1m,
        SpeedIndexEnabled = false,
        SpeedIndexMaximum = 3400,
        UpdatedAt = now,
        Version = 1
    };
}

internal sealed class PageAuditIncidentPolicyConfiguration
    : IEntityTypeConfiguration<PageAuditIncidentPolicyEntity>
{
    public void Configure(EntityTypeBuilder<PageAuditIncidentPolicyEntity> builder)
    {
        builder.ToTable("page_audit_incident_policy", table =>
        {
            table.HasCheckConstraint(
                "ck_page_audit_incident_policy_scores",
                "performance_minimum_score BETWEEN 0 AND 100 "
                + "AND accessibility_minimum_score BETWEEN 0 AND 100 "
                + "AND best_practices_minimum_score BETWEEN 0 AND 100 "
                + "AND seo_minimum_score BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "ck_page_audit_incident_policy_metric_limits",
                "first_contentful_paint_maximum BETWEEN 0 AND 600000 "
                + "AND largest_contentful_paint_maximum BETWEEN 0 AND 600000 "
                + "AND total_blocking_time_maximum BETWEEN 0 AND 600000 "
                + "AND cumulative_layout_shift_maximum BETWEEN 0 AND 10 "
                + "AND speed_index_maximum BETWEEN 0 AND 600000");
        });
        builder.HasKey(policy => policy.EndpointId);
        builder.Property(policy => policy.FirstContentfulPaintMaximum).HasPrecision(14, 4);
        builder.Property(policy => policy.LargestContentfulPaintMaximum).HasPrecision(14, 4);
        builder.Property(policy => policy.TotalBlockingTimeMaximum).HasPrecision(14, 4);
        builder.Property(policy => policy.CumulativeLayoutShiftMaximum).HasPrecision(14, 4);
        builder.Property(policy => policy.SpeedIndexMaximum).HasPrecision(14, 4);
        builder.Property(policy => policy.Version).IsConcurrencyToken();
        builder.HasOne(policy => policy.Endpoint).WithOne()
            .HasForeignKey<PageAuditIncidentPolicyEntity>(policy => policy.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(policy => policy.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

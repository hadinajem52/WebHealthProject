using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>Column bounds, in one place so the reader and the writer cannot disagree.</summary>
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
    public const int GroupName = 100;
    public const int Title = 500;
    public const int Description = 2000;
    public const int DisplayValue = 1000;
    public const int Explanation = 2000;
    public const int ErrorMessage = 1000;
}

internal sealed class PageAuditTargetConfiguration : IEntityTypeConfiguration<PageAuditTarget>
{
    /// <summary>
    /// Six hours to thirty days. The floor is not a performance limit but a courtesy one: each run
    /// asks Google to load somebody's page, and a tighter cadence spends quota faster than the
    /// score can meaningfully change.
    /// </summary>
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
                "category IN ('Seo')");

            table.HasCheckConstraint(
                "ck_page_audit_target_strategy",
                "strategy IN ('Mobile', 'Desktop')");

            table.HasCheckConstraint(
                "ck_page_audit_target_interval",
                $"interval_seconds BETWEEN {MinimumIntervalSeconds} AND {MaximumIntervalSeconds}");

            // Scheduling is a setting inside the feature, not beside it. Without this a disabled
            // target could still hold scheduling_enabled, and re-enabling it would silently
            // resume a cadence nobody re-approved.
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

        // One configuration per endpoint per audit profile. Enabling mobile must not overwrite
        // desktop, and two concurrent writers must not each create their own row.
        builder.HasIndex(target => new
        {
            target.EndpointId,
            target.Provider,
            target.Category,
            target.Strategy
        }).IsUnique().HasDatabaseName("ux_page_audit_target_profile");

        // The dispatcher's claim, as a partial index: only enabled scheduled targets are ever due,
        // and indexing the rest would grow the index with rows the query never reads.
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

            table.HasCheckConstraint("ck_page_audit_run_category", "category IN ('Seo')");

            table.HasCheckConstraint(
                "ck_page_audit_run_strategy",
                "strategy IN ('Mobile', 'Desktop')");

            // A score outside the provider's own range is a response we did not understand, not a
            // bad page. Storing one would put a number in the history that Google never sent.
            table.HasCheckConstraint(
                "ck_page_audit_run_raw_score",
                "raw_score IS NULL OR raw_score BETWEEN 0 AND 1");

            table.HasCheckConstraint("ck_page_audit_run_attempt_count", "attempt_count >= 0");

            // A run that has stopped has a finish time, and one still live does not. Without this
            // a killed worker could leave a terminal run that looks like it never ended.
            table.HasCheckConstraint(
                "ck_page_audit_run_finished_when_terminal",
                "(status IN ('Queued', 'Running')) = (finished_at IS NULL)");

            table.HasCheckConstraint(
                "ck_page_audit_run_finished_after_queued",
                "finished_at IS NULL OR finished_at >= queued_at");

            // A completed run carries a score and no failure. This is the constraint that keeps
            // "the page scored badly" and "we never got a score" from being stored alike.
            table.HasCheckConstraint(
                "ck_page_audit_run_completed_contract",
                "status NOT IN ('Completed', 'CompletedWithWarnings') "
                + "OR (raw_score IS NOT NULL AND failure_category IS NULL "
                + "AND lighthouse_version IS NOT NULL)");

            // A failed run says why, from the bounded vocabulary. A failure with no category tells
            // a reader only that something went wrong. The converse is deliberately not asserted:
            // a run still retrying carries the category of the attempt that just failed, which is
            // how the UI can say "retrying after a timeout" instead of "queued".
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

            // The claim is one fact in two columns, so it is present or absent as a whole.
            table.HasCheckConstraint(
                "ck_page_audit_run_lease_pair",
                "(lease_token IS NULL) = (lease_expires_at IS NULL)");

            // A terminal run holds no claim. Leaving one behind would make a finished run look
            // like work in progress to the reconciliation sweep.
            table.HasCheckConstraint(
                "ck_page_audit_run_terminal_has_no_lease",
                "status IN ('Queued', 'Running') OR lease_token IS NULL");
        });

        builder.HasKey(run => run.Id);
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

        // 0-1 with four decimals. Lighthouse reports two, and the headroom costs nothing while
        // making a provider that reports more precision a stored value rather than a rounding.
        builder.Property(run => run.RawScore).HasPrecision(5, 4);

        // Latest-first history for one target, served by the index rather than sorted. The id
        // breaks the tie so two runs finishing in the same microsecond still order stably.
        builder.HasIndex(run => new { run.PageAuditTargetId, run.FinishedAt, run.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_page_audit_run_target_finished");

        builder.HasIndex(run => new { run.EndpointId, run.FinishedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_page_audit_run_endpoint_finished");

        // The reconciliation sweep's query: non-terminal runs, oldest first.
        builder.HasIndex(run => new { run.Status, run.UpdatedAt })
            .HasDatabaseName("ix_page_audit_run_status_updated");

        // At most one live run per target. This is what stops a dispatcher and a person pressing
        // Run now from spending two API calls on the same audit, and what makes a spurious
        // re-enqueue harmless rather than duplicating work.
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

            // A passed or failed audit is one Lighthouse scored. Without this an audit could be
            // stored as Passed with no score behind it, which is a claim nothing supports.
            table.HasCheckConstraint(
                "ck_page_audit_item_scored_statuses_have_a_score",
                "status NOT IN ('Passed', 'Failed', 'Scored') OR score IS NOT NULL");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.AuditId).HasMaxLength(PageAuditTextBounds.AuditId).IsRequired();
        builder.Property(item => item.Status).HasMaxLength(PageAuditTextBounds.Status).IsRequired();
        builder.Property(item => item.ScoreDisplayMode).HasMaxLength(PageAuditTextBounds.ScoreDisplayMode);
        builder.Property(item => item.GroupName).HasMaxLength(PageAuditTextBounds.GroupName);
        builder.Property(item => item.Title).HasMaxLength(PageAuditTextBounds.Title);
        builder.Property(item => item.Description).HasMaxLength(PageAuditTextBounds.Description);
        builder.Property(item => item.DisplayValue).HasMaxLength(PageAuditTextBounds.DisplayValue);
        builder.Property(item => item.Explanation).HasMaxLength(PageAuditTextBounds.Explanation);
        builder.Property(item => item.ErrorMessage).HasMaxLength(PageAuditTextBounds.ErrorMessage);
        builder.Property(item => item.Score).HasPrecision(5, 4);

        // One row per audit per run. A retried finalization must update the run's items, never
        // append a second copy of every audit.
        builder.HasIndex(item => new { item.RunId, item.AuditId })
            .IsUnique()
            .HasDatabaseName("ux_page_audit_item_run_audit");

        // The detail sections read one run's items filtered by status, on the row that carries it.
        builder.HasIndex(item => new { item.RunId, item.Status, item.AuditId })
            .HasDatabaseName("ix_page_audit_item_run_status");

        // RESTRICT like every other foreign key here. A cascade would be the one place in this
        // schema where deleting a parent silently took evidence with it, and nothing needs it:
        // the endpoint purge deletes items explicitly, in order, before the runs they belong to.
        builder.HasOne(item => item.Run).WithMany(run => run.Items)
            .HasForeignKey(item => item.RunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

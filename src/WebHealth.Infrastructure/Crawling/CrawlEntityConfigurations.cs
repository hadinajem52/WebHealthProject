using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlRunConfiguration : IEntityTypeConfiguration<CrawlRun>
{
    public const int MaxSeedUrlsLength = 8192;

    public const int MaxScopeLength = 4096;

    public const int MaxFailureReasonLength = 1000;

    public void Configure(EntityTypeBuilder<CrawlRun> builder)
    {
        builder.ToTable("crawl_run", table =>
        {
            table.HasCheckConstraint(
                "ck_crawl_run_status",
                "status IN ('Running', 'Completed', 'Cancelled', 'Failed')");

            table.HasCheckConstraint(
                "ck_crawl_run_stop_reason",
                "stop_reason IN ('FrontierExhausted', 'PageLimit', 'DurationLimit', "
                + "'Cancelled', 'Failed')");

            table.HasCheckConstraint(
                "ck_crawl_run_status_stop_reason",
                "(status = 'Running') OR "
                + "(status = 'Completed' AND stop_reason IN "
                + "('FrontierExhausted', 'PageLimit', 'DurationLimit')) OR "
                + "(status = 'Cancelled' AND stop_reason = 'Cancelled') OR "
                + "(status = 'Failed' AND stop_reason = 'Failed')");

            table.HasCheckConstraint(
                "ck_crawl_run_override",
                "(robots_override_granted AND robots_override_refused_because IS NULL) OR "
                + "(NOT robots_override_granted AND robots_override_refused_because IS NOT NULL)");

            table.HasCheckConstraint(
                "ck_crawl_run_finished_after_started",
                "finished_at IS NULL OR finished_at >= started_at");

            table.HasCheckConstraint(
                "ck_crawl_run_finished_when_terminal",
                "(status = 'Running') = (finished_at IS NULL)");

            table.HasCheckConstraint(
                "ck_crawl_run_counts",
                "pages_fetched >= 0 AND links_recorded >= 0");

            table.HasCheckConstraint(
                "ck_crawl_run_limits",
                "max_pages > 0 AND max_depth >= 0");

            table.HasCheckConstraint(
                "ck_crawl_run_query_policy",
                "query_policy IN ('Canonicalize', 'PreserveOrder', 'Ignore')");

            table.HasCheckConstraint(
                "ck_crawl_run_failure_reason",
                "(status = 'Failed') OR (failure_reason IS NULL)");
        });

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Status).HasMaxLength(20).IsRequired();
        builder.Property(run => run.StopReason).HasMaxLength(30).IsRequired();
        builder.Property(run => run.SeedUrls).HasMaxLength(MaxSeedUrlsLength).IsRequired();
        builder.Property(run => run.RobotsOverrideRefusedBecause).HasMaxLength(40);
        builder.Property(run => run.QueryPolicy).HasMaxLength(20).IsRequired();
        builder.Property(run => run.AllowedHosts).HasMaxLength(MaxScopeLength);
        builder.Property(run => run.AllowedPathPrefixes).HasMaxLength(MaxScopeLength);
        builder.Property(run => run.FailureReason).HasMaxLength(MaxFailureReasonLength);
        builder.Property(run => run.CoverageLimited).HasDefaultValue(false);

        builder.HasIndex(run => new { run.EndpointId, run.StartedAt })
            .IsDescending(false, true);

        builder.HasIndex(run => run.EndpointId)
            .IsUnique()
            .HasFilter("status = 'Running'")
            .HasDatabaseName("ux_crawl_run_active");

        builder.HasOne(run => run.Endpoint).WithMany()
            .HasForeignKey(run => run.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CrawlLinkResultConfiguration : IEntityTypeConfiguration<CrawlLinkResult>
{
    public void Configure(EntityTypeBuilder<CrawlLinkResult> builder)
    {
        builder.ToTable("crawl_link_result", table =>
        {
            table.HasCheckConstraint(
                "ck_crawl_link_result_classification",
                "classification IN ('Healthy', 'Redirected', 'Broken', 'Blocked', "
                + "'Timeout', 'Skipped', 'Unknown')");

            table.HasCheckConstraint(
                "ck_crawl_link_result_skip_reason",
                "(classification IN ('Skipped', 'Unknown')) OR (skip_reason IS NULL)");

            table.HasCheckConstraint(
                "ck_crawl_link_result_source_hash",
                "(source_url IS NULL) = (source_url_hash IS NULL)");

            table.HasCheckConstraint(
                "ck_crawl_link_result_status_code",
                "status_code IS NULL OR status_code BETWEEN 100 AND 599");

            table.HasCheckConstraint(
                "ck_crawl_link_result_redirect_count",
                "redirect_count >= 0");

            table.HasCheckConstraint("ck_crawl_link_result_depth", "depth >= -1");

            table.HasCheckConstraint(
                "ck_crawl_link_result_duration",
                "duration_ms IS NULL OR duration_ms >= 0");
        });

        builder.HasKey(result => result.Id);
        builder.Property(result => result.SourceUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength);
        builder.Property(result => result.SourceUrlHash).HasColumnType("bytea");
        builder.Property(result => result.TargetUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength).IsRequired();
        builder.Property(result => result.TargetUrlHash).HasColumnType("bytea").IsRequired();
        builder.Property(result => result.Classification).HasMaxLength(20).IsRequired();
        builder.Property(result => result.SkipReason).HasMaxLength(40);
        builder.Property(result => result.FinalUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength);

        builder.HasIndex(result => new { result.RunId, result.SourceUrlHash, result.TargetUrlHash })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_crawl_link_result_pair");

        builder.HasIndex(result => new { result.RunId, result.Classification })
            .HasDatabaseName("ix_crawl_link_result_run_classification");

        builder.HasOne(result => result.Run).WithMany(run => run.Links)
            .HasForeignKey(result => result.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

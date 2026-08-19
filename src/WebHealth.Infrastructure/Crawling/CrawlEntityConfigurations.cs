using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlRunConfiguration : IEntityTypeConfiguration<CrawlRun>
{
    /// <summary>Enough for a realistic seed list; a run is configured, not discovered.</summary>
    public const int MaxSeedUrlsLength = 8192;

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

            // BR-L10 in the database rather than in a convention: a cancelled run can never be
            // stored as complete, and a completed run can never claim it was cancelled. A partial
            // crawl reported as a clean completed run is worse than no crawl at all.
            table.HasCheckConstraint(
                "ck_crawl_run_status_stop_reason",
                "(status = 'Running') OR "
                + "(status = 'Completed' AND stop_reason IN "
                + "('FrontierExhausted', 'PageLimit', 'DurationLimit')) OR "
                + "(status = 'Cancelled' AND stop_reason = 'Cancelled') OR "
                + "(status = 'Failed' AND stop_reason = 'Failed')");

            // BR-L02: a granted override carries no refusal, a refused one carries its reason.
            // An override that left no trace would be the silent flag this project refuses to have.
            table.HasCheckConstraint(
                "ck_crawl_run_override",
                "(robots_override_granted AND robots_override_refused_because IS NULL) OR "
                + "(NOT robots_override_granted AND robots_override_refused_because IS NOT NULL)");

            table.HasCheckConstraint(
                "ck_crawl_run_finished_after_started",
                "finished_at IS NULL OR finished_at >= started_at");

            // A run that has stopped has a finish time, and one still running does not. Without
            // this an interrupted process could leave a terminal run that looks like it never ended.
            table.HasCheckConstraint(
                "ck_crawl_run_finished_when_terminal",
                "(status = 'Running') = (finished_at IS NULL)");

            table.HasCheckConstraint(
                "ck_crawl_run_counts",
                "pages_fetched >= 0 AND links_recorded >= 0");
        });

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Status).HasMaxLength(20).IsRequired();
        builder.Property(run => run.StopReason).HasMaxLength(30).IsRequired();
        builder.Property(run => run.SeedUrls).HasMaxLength(MaxSeedUrlsLength).IsRequired();
        builder.Property(run => run.RobotsOverrideRefusedBecause).HasMaxLength(40);

        // The reports and the comparison both ask for an endpoint's runs newest first, so that is
        // the index. Descending on started_at so the ordering is served rather than sorted.
        builder.HasIndex(run => new { run.EndpointId, run.StartedAt })
            .IsDescending(false, true);

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

            // A skip is a recorded decision with a reason. Without this, "nobody looked at this"
            // and "this link is fine" could be stored as the same row.
            table.HasCheckConstraint(
                "ck_crawl_link_result_skip_reason",
                "(classification IN ('Skipped', 'Unknown')) OR (skip_reason IS NULL)");

            // The hash is identity, so it is present exactly when the URL it identifies is.
            table.HasCheckConstraint(
                "ck_crawl_link_result_source_hash",
                "(source_url IS NULL) = (source_url_hash IS NULL)");

            table.HasCheckConstraint(
                "ck_crawl_link_result_status_code",
                "status_code IS NULL OR status_code BETWEEN 100 AND 599");

            table.HasCheckConstraint(
                "ck_crawl_link_result_redirect_count",
                "redirect_count >= 0");

            // -1 is the recorded "no depth assigned", which happens when a run stops before the
            // frontier admitted the target. Anything below that is a bug, not a value.
            table.HasCheckConstraint("ck_crawl_link_result_depth", "depth >= -1");
        });

        builder.HasKey(result => result.Id);
        builder.Property(result => result.SourceUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength);
        builder.Property(result => result.SourceUrlHash).HasColumnType("bytea");
        builder.Property(result => result.TargetUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength).IsRequired();
        builder.Property(result => result.TargetUrlHash).HasColumnType("bytea").IsRequired();
        builder.Property(result => result.Classification).HasMaxLength(20).IsRequired();
        builder.Property(result => result.SkipReason).HasMaxLength(40);
        builder.Property(result => result.FinalUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength);

        // BR-L07. Over hashes rather than the URLs: a btree entry cannot hold two full 2048-character
        // URLs. NULLS NOT DISTINCT so a seed, whose source is null, still occurs exactly once —
        // the default treats every null as unique, which would let one seed be inserted repeatedly.
        builder.HasIndex(result => new { result.RunId, result.SourceUrlHash, result.TargetUrlHash })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_crawl_link_result_pair");

        // The reporting filter, on the row that carries it. Reaching classification through a join
        // to crawl_run is the exact shape Phase 5 lost time to, where the filter predicate and the
        // window predicate lived on different tables and no index could serve both.
        builder.HasIndex(result => new { result.RunId, result.Classification })
            .HasDatabaseName("ix_crawl_link_result_run_classification");

        builder.HasOne(result => result.Run).WithMany(run => run.Links)
            .HasForeignKey(result => result.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

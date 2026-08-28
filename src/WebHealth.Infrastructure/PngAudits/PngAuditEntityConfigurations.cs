using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.PngAudits;

public static class PngAuditTextBounds
{
    public const int Scope = 8192;
    public const int QueryParameters = 4096;
    public const int Status = 30;
    public const int Source = 20;
    public const int FailureCode = 50;
    public const int Diagnostic = 1000;
    public const int Profile = 100;
    public const int ContentType = 200;
    public const int Format = 30;
    public const int Classification = 40;
    public const int ReasonCode = 50;
    public const int Recommendation = 30;
    public const int AttributeKind = 40;
    public const int Descriptor = 100;
    public const int RawValue = 512;
    public const int CoverageArea = 30;
}

internal sealed class PngAuditRunConfiguration : IEntityTypeConfiguration<PngAuditRun>
{
    public void Configure(EntityTypeBuilder<PngAuditRun> builder)
    {
        builder.ToTable("png_audit_run", table =>
        {
            table.HasCheckConstraint(
                "ck_png_audit_run_status",
                "status IN ('Queued', 'Running', 'Completed', 'CompletedWithWarnings', "
                + "'Failed', 'Cancelled')");
            table.HasCheckConstraint(
                "ck_png_audit_run_source",
                "source IN ('Scheduled', 'Manual')");
            table.HasCheckConstraint(
                "ck_png_audit_run_query_policy",
                "query_policy IN ('Canonicalize', 'PreserveOrder', 'Ignore')");
            table.HasCheckConstraint(
                "ck_png_audit_run_lifecycle",
                "(status = 'Queued' AND finished_at IS NULL AND lease_token IS NULL "
                + "AND lease_expires_at IS NULL) OR "
                + "(status = 'Running' AND started_at IS NOT NULL AND finished_at IS NULL "
                + "AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR "
                + "(status IN ('Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled') "
                + "AND finished_at IS NOT NULL AND lease_token IS NULL AND lease_expires_at IS NULL)");
            table.HasCheckConstraint(
                "ck_png_audit_run_failure",
                "(status IN ('Failed', 'Cancelled')) = (failure_code IS NOT NULL) AND "
                + "(failure_code IS NULL OR failure_code IN ('WorkerUnavailable', 'TargetChanged', "
                + "'TargetIneligible', 'AttemptsExhausted', 'StorageUnavailable', 'Cancelled', "
                + "'Unexpected')) AND (status <> 'Cancelled' OR failure_code = 'Cancelled') AND "
                + "(status <> 'Failed' OR failure_code <> 'Cancelled')");
            table.HasCheckConstraint(
                "ck_png_audit_run_times",
                "updated_at >= queued_at "
                + "AND (started_at IS NULL OR started_at >= queued_at) "
                + "AND (finished_at IS NULL OR finished_at >= COALESCE(started_at, queued_at))");
            table.HasCheckConstraint(
                "ck_png_audit_run_counts",
                "attempt_count >= 0 AND pages_discovered >= 0 AND images_discovered >= 0 "
                + "AND images_analyzed >= 0 AND recommendation_count >= 0 "
                + "AND discovery_skip_count >= 0 AND http_attempts >= 0 "
                + "AND total_page_bytes >= 0 AND total_image_bytes >= 0 "
                + "AND images_analyzed <= images_discovered "
                + "AND recommendation_count <= images_analyzed "
                + "AND pages_discovered <= max_pages "
                + "AND images_discovered <= max_unique_images "
                + "AND http_attempts <= max_total_http_attempts "
                + "AND total_page_bytes <= max_total_page_bytes "
                + "AND total_image_bytes <= max_total_image_bytes");
            table.HasCheckConstraint(
                "ck_png_audit_run_page_limits",
                "max_pages BETWEEN 1 AND 1000 AND max_depth BETWEEN 0 AND 10 "
                + $"AND max_page_bytes BETWEEN 1 AND {2 * 1024 * 1024} "
                + "AND max_total_page_bytes >= max_page_bytes "
                + $"AND max_total_page_bytes <= {512L * 1024 * 1024} "
                + "AND max_image_references_per_page BETWEEN 1 AND 10000");
            table.HasCheckConstraint(
                "ck_png_audit_run_image_limits",
                "max_unique_images BETWEEN 1 AND 5000 "
                + "AND max_total_image_source_mappings >= max_unique_images "
                + "AND max_total_image_source_mappings <= 50000 "
                + $"AND max_image_bytes BETWEEN 1 AND {8 * 1024 * 1024} "
                + "AND max_total_image_bytes >= max_image_bytes "
                + $"AND max_total_image_bytes <= {1024L * 1024 * 1024} "
                + "AND max_width BETWEEN 1 AND 100000 AND max_height BETWEEN 1 AND 100000 "
                + "AND max_decoded_pixels BETWEEN 1 AND 1000000000 "
                + $"AND max_decoded_memory_bytes BETWEEN 1 AND {4L * 1024 * 1024 * 1024}");
            table.HasCheckConstraint(
                "ck_png_audit_run_fetch_limits",
                "max_total_http_attempts BETWEEN 1 AND 100000 "
                + "AND fetch_timeout_seconds BETWEEN 1 AND 120 "
                + "AND requests_per_second_per_host BETWEEN 0 AND 10 "
                + "AND transient_retry_count BETWEEN 0 AND 3 "
                + "AND max_duration_seconds BETWEEN 1 AND 14400 "
                + "AND max_query_parameters BETWEEN 1 AND 100");
            table.HasCheckConstraint(
                "ck_png_audit_run_thresholds",
                "min_savings_percent BETWEEN 0 AND 100 AND min_savings_bytes >= 0");
        });

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Source).HasMaxLength(PngAuditTextBounds.Source).IsRequired();
        builder.Property(run => run.Status).HasMaxLength(PngAuditTextBounds.Status).IsRequired();
        builder.Property(run => run.FailureCode).HasMaxLength(PngAuditTextBounds.FailureCode);
        builder.Property(run => run.SafeDiagnostic).HasMaxLength(PngAuditTextBounds.Diagnostic);
        builder.Property(run => run.SeedUrlSnapshot).HasMaxLength(CrawlUrlOptions.MaxUrlLength).IsRequired();
        builder.Property(run => run.SeedUrlIdentityHash).IsRequired();
        builder.Property(run => run.AllowedPageHosts).HasMaxLength(PngAuditTextBounds.Scope).IsRequired();
        builder.Property(run => run.AllowedPagePathPrefixes).HasMaxLength(PngAuditTextBounds.Scope).IsRequired();
        builder.Property(run => run.AllowedAssetHosts).HasMaxLength(PngAuditTextBounds.Scope).IsRequired();
        builder.Property(run => run.QueryPolicy).HasMaxLength(20).IsRequired();
        builder.Property(run => run.TrackingQueryParameters)
            .HasMaxLength(PngAuditTextBounds.QueryParameters).IsRequired();
        builder.Property(run => run.SensitiveQueryParameters)
            .HasMaxLength(PngAuditTextBounds.QueryParameters).IsRequired();
        builder.Property(run => run.AnalyzerProfile).HasMaxLength(PngAuditTextBounds.Profile).IsRequired();
        builder.Property(run => run.ComparisonProfile).HasMaxLength(PngAuditTextBounds.Profile).IsRequired();
        builder.Property(run => run.MinSavingsPercent).HasPrecision(7, 4);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_png_audit_run_seed_hash",
            "octet_length(seed_url_identity_hash) = 32"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_png_audit_run_archived_status",
            "archived_at IS NULL OR status IN ('Completed', 'CompletedWithWarnings', "
            + "'Failed', 'Cancelled')"));

        builder.HasIndex(run => new { run.EndpointId, run.ArchivedAt })
            .HasDatabaseName("ix_png_audit_run_endpoint_archived");
        builder.HasIndex(run => run.EndpointId)
            .IsUnique()
            .HasFilter("status IN ('Queued', 'Running')")
            .HasDatabaseName("ux_png_audit_run_active");
        builder.HasIndex(run => new { run.EndpointId, run.QueuedAt, run.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_png_audit_run_endpoint_queued");
        builder.HasIndex(run => new { run.Status, run.UpdatedAt, run.Id })
            .HasDatabaseName("ix_png_audit_run_reconcile");

        builder.HasOne(run => run.Endpoint).WithMany()
            .HasForeignKey(run => run.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(run => run.InitiatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PngAuditImageResultConfiguration
    : IEntityTypeConfiguration<PngAuditImageResult>
{
    public void Configure(EntityTypeBuilder<PngAuditImageResult> builder)
    {
        builder.ToTable("png_audit_image_result", table =>
        {
            table.HasCheckConstraint(
                "ck_png_audit_image_result_classification",
                "classification IN ('FetchFailed', 'HttpNonSuccess', 'ResponseTruncated', "
                + "'NotPng', 'IdentificationFailed', 'DimensionsExceeded', "
                + "'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', "
                + "'DecodeFailed', 'HighBitDepthPng', 'ColorProfileUnsupported', "
                + "'ComparisonUnavailable', 'VerifiedWebpCandidate', "
                + "'OptimizedPngPreferred', 'BelowWebpThreshold')");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_hashes",
                "octet_length(image_identity_hash) = 32 AND "
                + "((final_display_url IS NULL AND final_identity_hash IS NULL) OR "
                + "(final_display_url IS NOT NULL AND final_identity_hash IS NOT NULL "
                + "AND octet_length(final_identity_hash) = 32))");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_transport",
                "response_bytes >= 0 AND "
                + "(http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599) AND "
                + "(classification = 'FetchFailed' OR final_display_url IS NOT NULL) AND "
                + "(classification <> 'HttpNonSuccess' OR "
                + "(http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299)) AND "
                + "(classification NOT IN ('ResponseTruncated', 'NotPng', 'IdentificationFailed', "
                + "'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', "
                + "'AnimatedPng', 'DecodeFailed', 'HighBitDepthPng', 'ColorProfileUnsupported', "
                + "'ComparisonUnavailable', 'VerifiedWebpCandidate', 'OptimizedPngPreferred', "
                + "'BelowWebpThreshold') "
                + "OR (http_status_code IS NOT NULL AND http_status_code BETWEEN 200 AND 299))");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_facts",
                "(width IS NULL AND height IS NULL AND frame_count IS NULL "
                + "AND pixel_count IS NULL AND bit_depth IS NULL AND color_type IS NULL) OR "
                + "(width IS NOT NULL AND height IS NOT NULL AND frame_count IS NOT NULL "
                + "AND pixel_count IS NOT NULL AND bit_depth IS NOT NULL AND color_type IS NOT NULL "
                + "AND width > 0 AND height > 0 AND frame_count > 0 "
                + "AND bit_depth IN (1, 2, 4, 8, 16) AND color_type IN (0, 2, 3, 4, 6) "
                + "AND pixel_count = width::bigint * height::bigint)");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_transparency",
                "(uses_transparency IS NULL AND transparent_pixel_count IS NULL "
                + "AND transparent_pixel_percent IS NULL "
                + "AND semi_transparent_pixel_count IS NULL "
                + "AND fully_transparent_pixel_count IS NULL "
                + "AND background_transparent_pixel_count IS NULL "
                + "AND interior_transparent_pixel_count IS NULL AND min_alpha IS NULL) OR "
                + "(uses_transparency IS NOT NULL AND transparent_pixel_count IS NOT NULL "
                + "AND transparent_pixel_percent IS NOT NULL AND pixel_count IS NOT NULL "
                + "AND semi_transparent_pixel_count IS NOT NULL "
                + "AND fully_transparent_pixel_count IS NOT NULL "
                + "AND background_transparent_pixel_count IS NOT NULL "
                + "AND interior_transparent_pixel_count IS NOT NULL AND min_alpha IS NOT NULL "
                + "AND semi_transparent_pixel_count >= 0 AND fully_transparent_pixel_count >= 0 "
                + "AND background_transparent_pixel_count >= 0 "
                + "AND interior_transparent_pixel_count >= 0 "
                + "AND min_alpha BETWEEN 0 AND 255 "
                + "AND transparent_pixel_count = "
                + "semi_transparent_pixel_count + fully_transparent_pixel_count "
                + "AND fully_transparent_pixel_count = "
                + "background_transparent_pixel_count + interior_transparent_pixel_count "
                + "AND transparent_pixel_count <= pixel_count "
                + "AND uses_transparency = (transparent_pixel_count > 0) "
                + "AND (min_alpha = 255) = (transparent_pixel_count = 0) "
                + "AND transparent_pixel_percent = "
                + "round(transparent_pixel_count * 100.0 / pixel_count, 4))");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_comparison",
                "(optimized_png_bytes IS NULL AND candidate_webp_bytes IS NULL "
                + "AND original_savings_bytes IS NULL AND original_savings_percent IS NULL "
                + "AND reference_savings_bytes IS NULL AND reference_savings_percent IS NULL) OR "
                + "(optimized_png_bytes IS NULL AND candidate_webp_bytes IS NOT NULL "
                + "AND original_savings_bytes IS NOT NULL "
                + "AND original_savings_percent IS NOT NULL "
                + "AND reference_savings_bytes IS NULL AND reference_savings_percent IS NULL "
                + "AND response_bytes > 0 AND candidate_webp_bytes > 0 "
                + "AND original_savings_bytes = response_bytes - candidate_webp_bytes "
                + "AND original_savings_percent = "
                + "round(original_savings_bytes * 100.0 / response_bytes, 4)) OR "
                + "(optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL "
                + "AND original_savings_bytes IS NOT NULL "
                + "AND original_savings_percent IS NOT NULL "
                + "AND reference_savings_bytes IS NOT NULL "
                + "AND reference_savings_percent IS NOT NULL "
                + "AND response_bytes > 0 AND optimized_png_bytes > 0 AND candidate_webp_bytes > 0 "
                + "AND original_savings_bytes = response_bytes - candidate_webp_bytes "
                + "AND reference_savings_bytes = "
                + "least(response_bytes, optimized_png_bytes) - candidate_webp_bytes "
                + "AND original_savings_percent = "
                + "round(original_savings_bytes * 100.0 / response_bytes, 4) "
                + "AND reference_savings_percent = round(reference_savings_bytes * 100.0 "
                + "/ least(response_bytes, optimized_png_bytes), 4))");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_recommendation",
                "(recommendation = 'None' AND suggested_format IS NULL) OR "
                + "(recommendation = 'LosslessWebp' AND suggested_format = 'WebP') OR "
                + "(recommendation = 'OptimizePng' AND suggested_format = 'PNG')");
            table.HasCheckConstraint(
                "ck_png_audit_image_result_state",
                "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 "
                + "AND uses_transparency IS NULL AND optimized_png_bytes IS NULL "
                + "AND recommendation = 'None') OR "
                + "(classification IN ('HighBitDepthPng', 'ColorProfileUnsupported') "
                + "AND frame_count = 1 AND uses_transparency IS NOT NULL "
                + "AND optimized_png_bytes IS NULL AND recommendation = 'None') OR "
                + "(classification = 'ComparisonUnavailable' AND frame_count = 1 "
                + "AND uses_transparency IS NOT NULL AND reason_code IS NOT NULL "
                + "AND recommendation = 'None') OR "
                + "(classification = 'VerifiedWebpCandidate' AND frame_count = 1 "
                + "AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL "
                + "AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR "
                + "(classification = 'OptimizedPngPreferred' AND frame_count = 1 "
                + "AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL "
                + "AND candidate_webp_bytes IS NOT NULL AND recommendation = 'OptimizePng') OR "
                + "(classification = 'BelowWebpThreshold' AND frame_count = 1 "
                + "AND uses_transparency IS NOT NULL AND candidate_webp_bytes IS NOT NULL "
                + "AND recommendation = 'None') OR "
                + "(classification = 'NotPng' AND detected_format IS NOT NULL "
                + "AND upper(detected_format) <> 'PNG' AND width IS NULL "
                + "AND optimized_png_bytes IS NULL AND recommendation = 'None') OR "
                + "(classification = 'FetchFailed' AND http_status_code IS NULL "
                + "AND reason_code IS NOT NULL AND width IS NULL "
                + "AND optimized_png_bytes IS NULL AND recommendation = 'None') OR "
                + "(classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL "
                + "AND http_status_code NOT BETWEEN 200 AND 299 "
                + "AND width IS NULL AND optimized_png_bytes IS NULL "
                + "AND recommendation = 'None') OR "
                + "(classification = 'ResponseTruncated' AND response_bytes > 0 "
                + "AND width IS NULL AND optimized_png_bytes IS NULL "
                + "AND recommendation = 'None') OR "
                + "(classification IN ('IdentificationFailed', 'DimensionsExceeded', "
                + "'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') "
                + "AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None')");
        });

        builder.HasKey(result => result.Id);
        builder.Property(result => result.ImageDisplayUrl)
            .HasMaxLength(CrawlUrlOptions.MaxUrlLength).IsRequired();
        builder.Property(result => result.ImageIdentityHash).HasColumnType("bytea").IsRequired();
        builder.Property(result => result.FinalDisplayUrl).HasMaxLength(CrawlUrlOptions.MaxUrlLength);
        builder.Property(result => result.FinalIdentityHash).HasColumnType("bytea");
        builder.Property(result => result.DeclaredContentType).HasMaxLength(PngAuditTextBounds.ContentType);
        builder.Property(result => result.DetectedFormat).HasMaxLength(PngAuditTextBounds.Format);
        builder.Property(result => result.Classification)
            .HasMaxLength(PngAuditTextBounds.Classification).IsRequired();
        builder.Property(result => result.ReasonCode).HasMaxLength(PngAuditTextBounds.ReasonCode);
        builder.Property(result => result.Recommendation)
            .HasMaxLength(PngAuditTextBounds.Recommendation).IsRequired();
        builder.Property(result => result.SuggestedFormat).HasMaxLength(PngAuditTextBounds.Format);
        builder.Property(result => result.TransparentPixelPercent).HasPrecision(7, 4);
        builder.Property(result => result.OriginalSavingsPercent).HasPrecision(14, 4);
        builder.Property(result => result.ReferenceSavingsPercent).HasPrecision(14, 4);

        builder.HasIndex(result => new { result.RunId, result.ImageIdentityHash })
            .IsUnique()
            .HasDatabaseName("ux_png_audit_image_result_identity");
        builder.HasIndex(result => new { result.RunId, result.Classification, result.Id })
            .HasDatabaseName("ix_png_audit_image_result_classification");
        builder.HasIndex(result => new { result.RunId, result.Recommendation, result.Id })
            .HasDatabaseName("ix_png_audit_image_result_recommendation");

        builder.HasOne(result => result.Run).WithMany(run => run.ImageResults)
            .HasForeignKey(result => result.RunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PngAuditImageSourceConfiguration
    : IEntityTypeConfiguration<PngAuditImageSource>
{
    public void Configure(EntityTypeBuilder<PngAuditImageSource> builder)
    {
        builder.ToTable("png_audit_image_source", table =>
        {
            table.HasCheckConstraint(
                "ck_png_audit_image_source_hash",
                "octet_length(source_page_identity_hash) = 32");
        });

        builder.HasKey(source => source.Id);
        builder.Property(source => source.SourcePageDisplayUrl)
            .HasMaxLength(CrawlUrlOptions.MaxUrlLength).IsRequired();
        builder.Property(source => source.SourcePageIdentityHash).HasColumnType("bytea").IsRequired();
        builder.Property(source => source.AttributeKind)
            .HasMaxLength(PngAuditTextBounds.AttributeKind).IsRequired();
        builder.Property(source => source.Descriptor).HasMaxLength(PngAuditTextBounds.Descriptor);

        builder.HasIndex(source => new
        {
            source.ImageResultId,
            source.SourcePageIdentityHash,
            source.AttributeKind,
            source.Descriptor
        }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_png_audit_image_source_reference");

        builder.HasOne(source => source.ImageResult).WithMany(result => result.Sources)
            .HasForeignKey(source => source.ImageResultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PngAuditDiscoverySkipConfiguration
    : IEntityTypeConfiguration<PngAuditDiscoverySkipEntity>
{
    public void Configure(EntityTypeBuilder<PngAuditDiscoverySkipEntity> builder)
    {
        builder.ToTable("png_audit_discovery_skip", table =>
        {
            table.HasCheckConstraint(
                "ck_png_audit_discovery_skip_hash",
                "octet_length(source_page_identity_hash) = 32");
            table.HasCheckConstraint(
                "ck_png_audit_discovery_skip_reason",
                "reason_code IN ('DataUrl', 'BlobUrl', 'UnsupportedScheme', "
                + "'CredentialsPresent', 'MalformedUrl', 'OverlongValue', "
                + "'ExternalAssetHost', 'ReferenceLimit', 'UniqueImageLimit', "
                + "'SourceMappingLimit')");
        });

        builder.HasKey(skip => skip.Id);
        builder.Property(skip => skip.SourcePageDisplayUrl)
            .HasMaxLength(CrawlUrlOptions.MaxUrlLength).IsRequired();
        builder.Property(skip => skip.SourcePageIdentityHash).HasColumnType("bytea").IsRequired();
        builder.Property(skip => skip.AttributeKind)
            .HasMaxLength(PngAuditTextBounds.AttributeKind).IsRequired();
        builder.Property(skip => skip.Descriptor).HasMaxLength(PngAuditTextBounds.Descriptor);
        builder.Property(skip => skip.BoundedSafeRawValue)
            .HasMaxLength(PngAuditTextBounds.RawValue).IsRequired();
        builder.Property(skip => skip.ReasonCode)
            .HasMaxLength(PngAuditTextBounds.ReasonCode).IsRequired();

        builder.HasIndex(skip => new
        {
            skip.RunId,
            skip.SourcePageIdentityHash,
            skip.AttributeKind,
            skip.Descriptor,
            skip.BoundedSafeRawValue,
            skip.ReasonCode
        }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_png_audit_discovery_skip_reference");

        builder.HasOne(skip => skip.Run).WithMany(run => run.DiscoverySkips)
            .HasForeignKey(skip => skip.RunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PngAuditCoverageReasonConfiguration
    : IEntityTypeConfiguration<PngAuditCoverageReasonEntity>
{
    public void Configure(EntityTypeBuilder<PngAuditCoverageReasonEntity> builder)
    {
        builder.ToTable("png_audit_coverage_reason", table =>
        {
            table.HasCheckConstraint(
                "ck_png_audit_coverage_reason_area",
                "area IN ('Crawl', 'ImageAnalysis', 'SourceMappings')");
            table.HasCheckConstraint(
                "ck_png_audit_coverage_reason_code",
                "reason_code IN ('PageLimit', 'DepthLimit', 'PageBodyTruncated', "
                + "'NavigationReferenceLimit', 'ImageReferenceLimit', 'UniqueImageLimit', "
                + "'TotalPageBytesLimit', 'TotalImageBytesLimit', 'SourceMappingLimit', "
                + "'RobotsDisallowed', "
                + "'DurationLimit', 'HttpAttemptLimit', 'PageFetchFailed', "
                + "'PageHttpNonSuccess', 'RedirectOutOfScope', 'DocumentNotInspected', "
                + "'QueryVariantLimit')");
            table.HasCheckConstraint("ck_png_audit_coverage_reason_count", "count > 0");
        });

        builder.HasKey(reason => new { reason.RunId, reason.Area, reason.ReasonCode });
        builder.Property(reason => reason.Area)
            .HasMaxLength(PngAuditTextBounds.CoverageArea).IsRequired();
        builder.Property(reason => reason.ReasonCode)
            .HasMaxLength(PngAuditTextBounds.ReasonCode).IsRequired();

        builder.HasOne(reason => reason.Run).WithMany(run => run.CoverageReasons)
            .HasForeignKey(reason => reason.RunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

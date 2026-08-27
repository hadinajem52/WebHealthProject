using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PngAuditPersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "png_audit_run",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    safe_diagnostic = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    seed_url_snapshot = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    is_production_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_page_hosts = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    allowed_page_path_prefixes = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    allowed_asset_hosts = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    query_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tracking_query_parameters = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    sensitive_query_parameters = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    max_query_parameters = table.Column<int>(type: "integer", nullable: false),
                    max_pages = table.Column<int>(type: "integer", nullable: false),
                    max_depth = table.Column<int>(type: "integer", nullable: false),
                    max_page_bytes = table.Column<int>(type: "integer", nullable: false),
                    max_total_page_bytes = table.Column<long>(type: "bigint", nullable: false),
                    max_image_references_per_page = table.Column<int>(type: "integer", nullable: false),
                    max_unique_images = table.Column<int>(type: "integer", nullable: false),
                    max_total_image_source_mappings = table.Column<int>(type: "integer", nullable: false),
                    max_image_bytes = table.Column<int>(type: "integer", nullable: false),
                    max_total_image_bytes = table.Column<long>(type: "bigint", nullable: false),
                    max_width = table.Column<int>(type: "integer", nullable: false),
                    max_height = table.Column<int>(type: "integer", nullable: false),
                    max_decoded_pixels = table.Column<long>(type: "bigint", nullable: false),
                    max_decoded_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    max_total_http_attempts = table.Column<int>(type: "integer", nullable: false),
                    fetch_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    requests_per_second_per_host = table.Column<double>(type: "double precision", nullable: false),
                    transient_retry_count = table.Column<int>(type: "integer", nullable: false),
                    max_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    min_savings_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    min_savings_bytes = table.Column<long>(type: "bigint", nullable: false),
                    analyzer_profile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    comparison_profile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pages_discovered = table.Column<int>(type: "integer", nullable: false),
                    images_discovered = table.Column<int>(type: "integer", nullable: false),
                    images_analyzed = table.Column<int>(type: "integer", nullable: false),
                    recommendation_count = table.Column<int>(type: "integer", nullable: false),
                    discovery_skip_count = table.Column<int>(type: "integer", nullable: false),
                    http_attempts = table.Column<int>(type: "integer", nullable: false),
                    total_page_bytes = table.Column<long>(type: "bigint", nullable: false),
                    total_image_bytes = table.Column<long>(type: "bigint", nullable: false),
                    crawl_coverage_limited = table.Column<bool>(type: "boolean", nullable: false),
                    image_analysis_coverage_limited = table.Column<bool>(type: "boolean", nullable: false),
                    source_mapping_coverage_limited = table.Column<bool>(type: "boolean", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_png_audit_run", x => x.id);
                    table.CheckConstraint("ck_png_audit_run_counts", "attempt_count >= 0 AND pages_discovered >= 0 AND images_discovered >= 0 AND images_analyzed >= 0 AND recommendation_count >= 0 AND discovery_skip_count >= 0 AND http_attempts >= 0 AND total_page_bytes >= 0 AND total_image_bytes >= 0 AND images_analyzed <= images_discovered AND recommendation_count <= images_analyzed");
                    table.CheckConstraint("ck_png_audit_run_failure", "(status IN ('Failed', 'Cancelled')) = (failure_code IS NOT NULL)");
                    table.CheckConstraint("ck_png_audit_run_fetch_limits", "max_total_http_attempts BETWEEN 1 AND 100000 AND fetch_timeout_seconds BETWEEN 1 AND 120 AND requests_per_second_per_host BETWEEN 0 AND 10 AND transient_retry_count BETWEEN 0 AND 3 AND max_duration_seconds BETWEEN 1 AND 14400 AND max_query_parameters BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_png_audit_run_image_limits", "max_unique_images BETWEEN 1 AND 5000 AND max_total_image_source_mappings >= max_unique_images AND max_total_image_source_mappings <= 50000 AND max_image_bytes BETWEEN 1 AND 8388608 AND max_total_image_bytes >= max_image_bytes AND max_total_image_bytes <= 1073741824 AND max_width BETWEEN 1 AND 100000 AND max_height BETWEEN 1 AND 100000 AND max_decoded_pixels BETWEEN 1 AND 1000000000 AND max_decoded_memory_bytes BETWEEN 1 AND 4294967296");
                    table.CheckConstraint("ck_png_audit_run_lifecycle", "(status = 'Queued' AND finished_at IS NULL AND lease_token IS NULL AND lease_expires_at IS NULL) OR (status = 'Running' AND started_at IS NOT NULL AND finished_at IS NULL AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR (status IN ('Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled') AND finished_at IS NOT NULL AND lease_token IS NULL AND lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_png_audit_run_page_limits", "max_pages BETWEEN 1 AND 1000 AND max_depth BETWEEN 0 AND 10 AND max_page_bytes BETWEEN 1 AND 2097152 AND max_total_page_bytes >= max_page_bytes AND max_total_page_bytes <= 536870912 AND max_image_references_per_page BETWEEN 1 AND 10000");
                    table.CheckConstraint("ck_png_audit_run_query_policy", "query_policy IN ('Canonicalize', 'PreserveOrder', 'Ignore')");
                    table.CheckConstraint("ck_png_audit_run_source", "source IN ('Scheduled', 'Manual')");
                    table.CheckConstraint("ck_png_audit_run_status", "status IN ('Queued', 'Running', 'Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled')");
                    table.CheckConstraint("ck_png_audit_run_thresholds", "min_savings_percent BETWEEN 0 AND 100 AND min_savings_bytes >= 0");
                    table.CheckConstraint("ck_png_audit_run_times", "updated_at >= queued_at AND (started_at IS NULL OR started_at >= queued_at) AND (finished_at IS NULL OR finished_at >= COALESCE(started_at, queued_at))");
                    table.ForeignKey(
                        name: "fk_png_audit_run_app_user_initiated_by_user_id",
                        column: x => x.initiated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_png_audit_run_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "png_audit_coverage_reason",
                schema: "web_health",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_png_audit_coverage_reason", x => new { x.run_id, x.area, x.reason_code });
                    table.CheckConstraint("ck_png_audit_coverage_reason_area", "area IN ('Crawl', 'ImageAnalysis', 'SourceMappings')");
                    table.CheckConstraint("ck_png_audit_coverage_reason_code", "reason_code IN ('PageLimit', 'DepthLimit', 'PageBodyTruncated', 'NavigationReferenceLimit', 'ImageReferenceLimit', 'UniqueImageLimit', 'TotalPageBytesLimit', 'TotalImageBytesLimit', 'SourceMappingLimit', 'RobotsDisallowed', 'DurationLimit', 'HttpAttemptLimit', 'PageFetchFailed', 'PageHttpNonSuccess', 'RedirectOutOfScope', 'DocumentNotInspected', 'QueryVariantLimit')");
                    table.CheckConstraint("ck_png_audit_coverage_reason_count", "count > 0");
                    table.ForeignKey(
                        name: "fk_png_audit_coverage_reason_png_audit_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "web_health",
                        principalTable: "png_audit_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "png_audit_discovery_skip",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_page_display_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_page_identity_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    attribute_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descriptor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bounded_safe_raw_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_png_audit_discovery_skip", x => x.id);
                    table.CheckConstraint("ck_png_audit_discovery_skip_hash", "octet_length(source_page_identity_hash) = 32");
                    table.CheckConstraint("ck_png_audit_discovery_skip_reason", "reason_code IN ('DataUrl', 'BlobUrl', 'UnsupportedScheme', 'CredentialsPresent', 'MalformedUrl', 'OverlongValue', 'ExternalAssetHost', 'ReferenceLimit', 'UniqueImageLimit', 'SourceMappingLimit')");
                    table.ForeignKey(
                        name: "fk_png_audit_discovery_skip_png_audit_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "web_health",
                        principalTable: "png_audit_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "png_audit_image_result",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_display_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    image_identity_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    final_display_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    final_identity_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    declared_content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    detected_format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    frame_count = table.Column<int>(type: "integer", nullable: true),
                    pixel_count = table.Column<long>(type: "bigint", nullable: true),
                    uses_transparency = table.Column<bool>(type: "boolean", nullable: true),
                    transparent_pixel_count = table.Column<long>(type: "bigint", nullable: true),
                    transparent_pixel_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    classification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    recommendation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    suggested_format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    normalized_png_bytes = table.Column<long>(type: "bigint", nullable: true),
                    candidate_webp_bytes = table.Column<long>(type: "bigint", nullable: true),
                    original_savings_bytes = table.Column<long>(type: "bigint", nullable: true),
                    original_savings_percent = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    normalized_savings_bytes = table.Column<long>(type: "bigint", nullable: true),
                    normalized_savings_percent = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_png_audit_image_result", x => x.id);
                    table.CheckConstraint("ck_png_audit_image_result_classification", "classification IN ('FetchFailed', 'HttpNonSuccess', 'ResponseTruncated', 'NotPng', 'IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', 'UsesTransparency', 'WebpComparisonFailed', 'OpaqueWebpCandidate', 'OpaqueBelowWebpThreshold')");
                    table.CheckConstraint("ck_png_audit_image_result_comparison", "(normalized_png_bytes IS NULL AND candidate_webp_bytes IS NULL AND original_savings_bytes IS NULL AND original_savings_percent IS NULL AND normalized_savings_bytes IS NULL AND normalized_savings_percent IS NULL) OR (normalized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND original_savings_bytes IS NOT NULL AND original_savings_percent IS NOT NULL AND normalized_savings_bytes IS NOT NULL AND normalized_savings_percent IS NOT NULL AND response_bytes > 0 AND normalized_png_bytes > 0 AND candidate_webp_bytes > 0 AND original_savings_bytes = response_bytes - candidate_webp_bytes AND normalized_savings_bytes = normalized_png_bytes - candidate_webp_bytes AND original_savings_percent = round(original_savings_bytes * 100.0 / response_bytes, 4) AND normalized_savings_percent = round(normalized_savings_bytes * 100.0 / normalized_png_bytes, 4))");
                    table.CheckConstraint("ck_png_audit_image_result_facts", "(width IS NULL AND height IS NULL AND frame_count IS NULL AND pixel_count IS NULL) OR (width IS NOT NULL AND height IS NOT NULL AND frame_count IS NOT NULL AND pixel_count IS NOT NULL AND width > 0 AND height > 0 AND frame_count > 0 AND pixel_count = width::bigint * height::bigint)");
                    table.CheckConstraint("ck_png_audit_image_result_hashes", "octet_length(image_identity_hash) = 32 AND ((final_display_url IS NULL AND final_identity_hash IS NULL) OR (final_display_url IS NOT NULL AND final_identity_hash IS NOT NULL AND octet_length(final_identity_hash) = 32))");
                    table.CheckConstraint("ck_png_audit_image_result_recommendation", "(recommendation = 'None' AND suggested_format IS NULL) OR (recommendation = 'LosslessWebp' AND suggested_format IS NOT NULL AND suggested_format = 'WebP')");
                    table.CheckConstraint("ck_png_audit_image_result_state", "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 AND uses_transparency IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'UsesTransparency' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS TRUE AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'WebpComparisonFailed' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS FALSE AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'OpaqueWebpCandidate' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS FALSE AND normalized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OpaqueBelowWebpThreshold' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS FALSE AND normalized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None')");
                    table.CheckConstraint("ck_png_audit_image_result_transparency", "(uses_transparency IS NULL AND transparent_pixel_count IS NULL AND transparent_pixel_percent IS NULL) OR (uses_transparency IS TRUE AND transparent_pixel_count IS NOT NULL AND transparent_pixel_percent IS NOT NULL AND pixel_count IS NOT NULL AND transparent_pixel_count > 0 AND transparent_pixel_count <= pixel_count AND transparent_pixel_percent = round(transparent_pixel_count * 100.0 / pixel_count, 4)) OR (uses_transparency IS FALSE AND transparent_pixel_count IS NOT NULL AND transparent_pixel_percent IS NOT NULL AND transparent_pixel_count = 0 AND transparent_pixel_percent = 0)");
                    table.CheckConstraint("ck_png_audit_image_result_transport", "response_bytes >= 0 AND (http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599) AND (classification = 'FetchFailed' OR final_display_url IS NOT NULL) AND (classification <> 'HttpNonSuccess' OR (http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299)) AND (classification NOT IN ('ResponseTruncated', 'NotPng', 'IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', 'UsesTransparency', 'WebpComparisonFailed', 'OpaqueWebpCandidate', 'OpaqueBelowWebpThreshold') OR (http_status_code IS NOT NULL AND http_status_code BETWEEN 200 AND 299))");
                    table.ForeignKey(
                        name: "fk_png_audit_image_result_png_audit_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "web_health",
                        principalTable: "png_audit_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "png_audit_image_source",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_page_display_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_page_identity_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    attribute_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descriptor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_png_audit_image_source", x => x.id);
                    table.CheckConstraint("ck_png_audit_image_source_hash", "octet_length(source_page_identity_hash) = 32");
                    table.ForeignKey(
                        name: "fk_png_audit_image_source_png_audit_image_result_image_result_~",
                        column: x => x.image_result_id,
                        principalSchema: "web_health",
                        principalTable: "png_audit_image_result",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_png_audit_discovery_skip_reference",
                schema: "web_health",
                table: "png_audit_discovery_skip",
                columns: new[] { "run_id", "source_page_identity_hash", "attribute_kind", "descriptor", "bounded_safe_raw_value", "reason_code" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_png_audit_image_result_classification",
                schema: "web_health",
                table: "png_audit_image_result",
                columns: new[] { "run_id", "classification", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_png_audit_image_result_recommendation",
                schema: "web_health",
                table: "png_audit_image_result",
                columns: new[] { "run_id", "recommendation", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_png_audit_image_result_identity",
                schema: "web_health",
                table: "png_audit_image_result",
                columns: new[] { "run_id", "image_identity_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_png_audit_image_source_reference",
                schema: "web_health",
                table: "png_audit_image_source",
                columns: new[] { "image_result_id", "source_page_identity_hash", "attribute_kind", "descriptor" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_png_audit_run_endpoint_queued",
                schema: "web_health",
                table: "png_audit_run",
                columns: new[] { "endpoint_id", "queued_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_png_audit_run_initiated_by_user_id",
                schema: "web_health",
                table: "png_audit_run",
                column: "initiated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_png_audit_run_reconcile",
                schema: "web_health",
                table: "png_audit_run",
                columns: new[] { "status", "updated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_png_audit_run_active",
                schema: "web_health",
                table: "png_audit_run",
                column: "endpoint_id",
                unique: true,
                filter: "status IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "png_audit_coverage_reason",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "png_audit_discovery_skip",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "png_audit_image_source",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "png_audit_image_result",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "png_audit_run",
                schema: "web_health");
        }
    }
}

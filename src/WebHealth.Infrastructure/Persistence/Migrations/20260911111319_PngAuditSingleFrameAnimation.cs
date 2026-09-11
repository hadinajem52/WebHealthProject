using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PngAuditSingleFrameAnimation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count >= 1 AND uses_transparency IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('HighBitDepthPng', 'ColorProfileUnsupported') AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ComparisonUnavailable' AND frame_count = 1 AND uses_transparency IS NOT NULL AND reason_code IS NOT NULL AND recommendation = 'None') OR (classification = 'VerifiedWebpCandidate' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OptimizedPngPreferred' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'OptimizePng') OR (classification = 'BelowWebpThreshold' AND frame_count = 1 AND uses_transparency IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.Sql(
                """
                DELETE FROM web_health.png_audit_image_source
                WHERE image_result_id IN (
                    SELECT id
                    FROM web_health.png_audit_image_result
                    WHERE classification = 'AnimatedPng'
                      AND frame_count = 1);
                """);
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE retired_png_audit_runs ON COMMIT DROP AS
                SELECT DISTINCT run_id
                FROM web_health.png_audit_image_result
                WHERE classification = 'AnimatedPng'
                  AND frame_count = 1;
                """);
            migrationBuilder.Sql(
                """
                DELETE FROM web_health.png_audit_image_result
                WHERE classification = 'AnimatedPng'
                  AND frame_count = 1;
                """);
            migrationBuilder.Sql(
                """
                UPDATE web_health.png_audit_run AS run
                SET images_discovered = totals.image_count,
                    images_analyzed = totals.analyzed_count,
                    recommendation_count = totals.recommendation_count,
                    total_image_bytes = totals.total_image_bytes
                FROM (
                    SELECT retired.run_id,
                           count(result.id) AS image_count,
                           count(result.id) FILTER (
                               WHERE result.classification NOT IN (
                                   'FetchFailed', 'HttpNonSuccess', 'ResponseTruncated')) AS analyzed_count,
                           count(result.id) FILTER (
                               WHERE result.recommendation = 'LosslessWebp') AS recommendation_count,
                           coalesce(sum(result.response_bytes), 0) AS total_image_bytes
                    FROM retired_png_audit_runs AS retired
                    LEFT JOIN web_health.png_audit_image_result AS result
                        ON result.run_id = retired.run_id
                    GROUP BY retired.run_id) AS totals
                WHERE run.id = totals.run_id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 AND uses_transparency IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('HighBitDepthPng', 'ColorProfileUnsupported') AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ComparisonUnavailable' AND frame_count = 1 AND uses_transparency IS NOT NULL AND reason_code IS NOT NULL AND recommendation = 'None') OR (classification = 'VerifiedWebpCandidate' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OptimizedPngPreferred' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'OptimizePng') OR (classification = 'BelowWebpThreshold' AND frame_count = 1 AND uses_transparency IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None')");
        }
    }
}

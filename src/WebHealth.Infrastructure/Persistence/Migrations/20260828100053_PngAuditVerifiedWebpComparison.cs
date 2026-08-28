using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PngAuditVerifiedWebpComparison : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.Sql(
                """
                UPDATE web_health.png_audit_image_result
                SET reason_code = 'ComparisonEngineUnavailable'
                WHERE classification = 'ComparisonUnavailable'
                  AND reason_code IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(optimized_png_bytes IS NULL AND candidate_webp_bytes IS NULL AND original_savings_bytes IS NULL AND original_savings_percent IS NULL AND reference_savings_bytes IS NULL AND reference_savings_percent IS NULL) OR (optimized_png_bytes IS NULL AND candidate_webp_bytes IS NOT NULL AND original_savings_bytes IS NOT NULL AND original_savings_percent IS NOT NULL AND reference_savings_bytes IS NULL AND reference_savings_percent IS NULL AND response_bytes > 0 AND candidate_webp_bytes > 0 AND original_savings_bytes = response_bytes - candidate_webp_bytes AND original_savings_percent = round(original_savings_bytes * 100.0 / response_bytes, 4)) OR (optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND original_savings_bytes IS NOT NULL AND original_savings_percent IS NOT NULL AND reference_savings_bytes IS NOT NULL AND reference_savings_percent IS NOT NULL AND response_bytes > 0 AND optimized_png_bytes > 0 AND candidate_webp_bytes > 0 AND original_savings_bytes = response_bytes - candidate_webp_bytes AND reference_savings_bytes = least(response_bytes, optimized_png_bytes) - candidate_webp_bytes AND original_savings_percent = round(original_savings_bytes * 100.0 / response_bytes, 4) AND reference_savings_percent = round(reference_savings_bytes * 100.0 / least(response_bytes, optimized_png_bytes), 4))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 AND uses_transparency IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('HighBitDepthPng', 'ColorProfileUnsupported') AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ComparisonUnavailable' AND frame_count = 1 AND uses_transparency IS NOT NULL AND reason_code IS NOT NULL AND recommendation = 'None') OR (classification = 'VerifiedWebpCandidate' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OptimizedPngPreferred' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'OptimizePng') OR (classification = 'BelowWebpThreshold' AND frame_count = 1 AND uses_transparency IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result");

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
                    WHERE candidate_webp_bytes IS NOT NULL
                      AND optimized_png_bytes IS NULL);
                """);
            migrationBuilder.Sql(
                """
                DELETE FROM web_health.png_audit_image_result
                WHERE candidate_webp_bytes IS NOT NULL
                  AND optimized_png_bytes IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(optimized_png_bytes IS NULL AND candidate_webp_bytes IS NULL AND original_savings_bytes IS NULL AND original_savings_percent IS NULL AND reference_savings_bytes IS NULL AND reference_savings_percent IS NULL) OR (optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND original_savings_bytes IS NOT NULL AND original_savings_percent IS NOT NULL AND reference_savings_bytes IS NOT NULL AND reference_savings_percent IS NOT NULL AND response_bytes > 0 AND optimized_png_bytes > 0 AND candidate_webp_bytes > 0 AND original_savings_bytes = response_bytes - candidate_webp_bytes AND reference_savings_bytes = least(response_bytes, optimized_png_bytes) - candidate_webp_bytes AND original_savings_percent = round(original_savings_bytes * 100.0 / response_bytes, 4) AND reference_savings_percent = round(reference_savings_bytes * 100.0 / least(response_bytes, optimized_png_bytes), 4))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 AND uses_transparency IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('HighBitDepthPng', 'ColorProfileUnsupported') AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ComparisonUnavailable' AND frame_count = 1 AND uses_transparency IS NOT NULL AND recommendation = 'None') OR (classification = 'VerifiedWebpCandidate' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OptimizedPngPreferred' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'OptimizePng') OR (classification = 'BelowWebpThreshold' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None')");
        }
    }
}

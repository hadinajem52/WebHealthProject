using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PngAuditComparisonModelV2 : Migration
    {
        /// <inheritdoc />
        private const string LegacyClassifications =
            "'FetchFailed', 'HttpNonSuccess', 'ResponseTruncated', 'NotPng', "
            + "'IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', "
            + "'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', "
            + "'UsesTransparency', 'WebpComparisonFailed', 'OpaqueWebpCandidate', "
            + "'OpaqueBelowWebpThreshold'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DELETE FROM web_health.png_audit_image_source
                WHERE image_result_id IN (
                    SELECT id
                    FROM web_health.png_audit_image_result
                    WHERE classification IN ({LegacyClassifications}));
                """);
            migrationBuilder.Sql(
                $"""
                DELETE FROM web_health.png_audit_image_result
                WHERE classification IN ({LegacyClassifications});
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_classification",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_facts",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_recommendation",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_transparency",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_transport",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.RenameColumn(
                name: "normalized_savings_percent",
                schema: "web_health",
                table: "png_audit_image_result",
                newName: "reference_savings_percent");

            migrationBuilder.RenameColumn(
                name: "normalized_savings_bytes",
                schema: "web_health",
                table: "png_audit_image_result",
                newName: "reference_savings_bytes");

            migrationBuilder.RenameColumn(
                name: "normalized_png_bytes",
                schema: "web_health",
                table: "png_audit_image_result",
                newName: "optimized_png_bytes");

            migrationBuilder.AddColumn<long>(
                name: "background_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bit_depth",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "color_type",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "fully_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "interior_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_alpha",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "semi_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_classification",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "classification IN ('FetchFailed', 'HttpNonSuccess', 'ResponseTruncated', 'NotPng', 'IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', 'HighBitDepthPng', 'ColorProfileUnsupported', 'ComparisonUnavailable', 'VerifiedWebpCandidate', 'OptimizedPngPreferred', 'BelowWebpThreshold')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(optimized_png_bytes IS NULL AND candidate_webp_bytes IS NULL AND original_savings_bytes IS NULL AND original_savings_percent IS NULL AND reference_savings_bytes IS NULL AND reference_savings_percent IS NULL) OR (optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND original_savings_bytes IS NOT NULL AND original_savings_percent IS NOT NULL AND reference_savings_bytes IS NOT NULL AND reference_savings_percent IS NOT NULL AND response_bytes > 0 AND optimized_png_bytes > 0 AND candidate_webp_bytes > 0 AND original_savings_bytes = response_bytes - candidate_webp_bytes AND reference_savings_bytes = least(response_bytes, optimized_png_bytes) - candidate_webp_bytes AND original_savings_percent = round(original_savings_bytes * 100.0 / response_bytes, 4) AND reference_savings_percent = round(reference_savings_bytes * 100.0 / least(response_bytes, optimized_png_bytes), 4))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_facts",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(width IS NULL AND height IS NULL AND frame_count IS NULL AND pixel_count IS NULL AND bit_depth IS NULL AND color_type IS NULL) OR (width IS NOT NULL AND height IS NOT NULL AND frame_count IS NOT NULL AND pixel_count IS NOT NULL AND bit_depth IS NOT NULL AND color_type IS NOT NULL AND width > 0 AND height > 0 AND frame_count > 0 AND bit_depth IN (1, 2, 4, 8, 16) AND color_type IN (0, 2, 3, 4, 6) AND pixel_count = width::bigint * height::bigint)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_recommendation",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(recommendation = 'None' AND suggested_format IS NULL) OR (recommendation = 'LosslessWebp' AND suggested_format = 'WebP') OR (recommendation = 'OptimizePng' AND suggested_format = 'PNG')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 AND uses_transparency IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('HighBitDepthPng', 'ColorProfileUnsupported') AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ComparisonUnavailable' AND frame_count = 1 AND uses_transparency IS NOT NULL AND recommendation = 'None') OR (classification = 'VerifiedWebpCandidate' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OptimizedPngPreferred' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'OptimizePng') OR (classification = 'BelowWebpThreshold' AND frame_count = 1 AND uses_transparency IS NOT NULL AND optimized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND optimized_png_bytes IS NULL AND recommendation = 'None')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_transparency",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(uses_transparency IS NULL AND transparent_pixel_count IS NULL AND transparent_pixel_percent IS NULL AND semi_transparent_pixel_count IS NULL AND fully_transparent_pixel_count IS NULL AND background_transparent_pixel_count IS NULL AND interior_transparent_pixel_count IS NULL AND min_alpha IS NULL) OR (uses_transparency IS NOT NULL AND transparent_pixel_count IS NOT NULL AND transparent_pixel_percent IS NOT NULL AND pixel_count IS NOT NULL AND semi_transparent_pixel_count IS NOT NULL AND fully_transparent_pixel_count IS NOT NULL AND background_transparent_pixel_count IS NOT NULL AND interior_transparent_pixel_count IS NOT NULL AND min_alpha IS NOT NULL AND semi_transparent_pixel_count >= 0 AND fully_transparent_pixel_count >= 0 AND background_transparent_pixel_count >= 0 AND interior_transparent_pixel_count >= 0 AND min_alpha BETWEEN 0 AND 255 AND transparent_pixel_count = semi_transparent_pixel_count + fully_transparent_pixel_count AND fully_transparent_pixel_count = background_transparent_pixel_count + interior_transparent_pixel_count AND transparent_pixel_count <= pixel_count AND uses_transparency = (transparent_pixel_count > 0) AND (min_alpha = 255) = (transparent_pixel_count = 0) AND transparent_pixel_percent = round(transparent_pixel_count * 100.0 / pixel_count, 4))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_transport",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "response_bytes >= 0 AND (http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599) AND (classification = 'FetchFailed' OR final_display_url IS NOT NULL) AND (classification <> 'HttpNonSuccess' OR (http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299)) AND (classification NOT IN ('ResponseTruncated', 'NotPng', 'IdentificationFailed', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', 'HighBitDepthPng', 'ColorProfileUnsupported', 'ComparisonUnavailable', 'VerifiedWebpCandidate', 'OptimizedPngPreferred', 'BelowWebpThreshold') OR (http_status_code IS NOT NULL AND http_status_code BETWEEN 200 AND 299))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DELETE FROM web_health.png_audit_image_source
                WHERE image_result_id IN (
                    SELECT id
                    FROM web_health.png_audit_image_result
                    WHERE classification NOT IN ({LegacyClassifications}));
                """);
            migrationBuilder.Sql(
                $"""
                DELETE FROM web_health.png_audit_image_result
                WHERE classification NOT IN ({LegacyClassifications});
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_classification",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_facts",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_recommendation",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_transparency",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_image_result_transport",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "background_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "bit_depth",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "color_type",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "fully_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "interior_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "min_alpha",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.DropColumn(
                name: "semi_transparent_pixel_count",
                schema: "web_health",
                table: "png_audit_image_result");

            migrationBuilder.RenameColumn(
                name: "reference_savings_bytes",
                schema: "web_health",
                table: "png_audit_image_result",
                newName: "normalized_savings_bytes");

            migrationBuilder.RenameColumn(
                name: "reference_savings_percent",
                schema: "web_health",
                table: "png_audit_image_result",
                newName: "normalized_savings_percent");

            migrationBuilder.RenameColumn(
                name: "optimized_png_bytes",
                schema: "web_health",
                table: "png_audit_image_result",
                newName: "normalized_png_bytes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_classification",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "classification IN ('FetchFailed', 'HttpNonSuccess', 'ResponseTruncated', 'NotPng', 'IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', 'UsesTransparency', 'WebpComparisonFailed', 'OpaqueWebpCandidate', 'OpaqueBelowWebpThreshold')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_comparison",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(normalized_png_bytes IS NULL AND candidate_webp_bytes IS NULL AND original_savings_bytes IS NULL AND original_savings_percent IS NULL AND normalized_savings_bytes IS NULL AND normalized_savings_percent IS NULL) OR (normalized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND original_savings_bytes IS NOT NULL AND original_savings_percent IS NOT NULL AND normalized_savings_bytes IS NOT NULL AND normalized_savings_percent IS NOT NULL AND response_bytes > 0 AND normalized_png_bytes > 0 AND candidate_webp_bytes > 0 AND original_savings_bytes = response_bytes - candidate_webp_bytes AND normalized_savings_bytes = normalized_png_bytes - candidate_webp_bytes AND original_savings_percent = round(original_savings_bytes * 100.0 / response_bytes, 4) AND normalized_savings_percent = round(normalized_savings_bytes * 100.0 / normalized_png_bytes, 4))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_facts",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(width IS NULL AND height IS NULL AND frame_count IS NULL AND pixel_count IS NULL) OR (width IS NOT NULL AND height IS NOT NULL AND frame_count IS NOT NULL AND pixel_count IS NOT NULL AND width > 0 AND height > 0 AND frame_count > 0 AND pixel_count = width::bigint * height::bigint)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_recommendation",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(recommendation = 'None' AND suggested_format IS NULL) OR (recommendation = 'LosslessWebp' AND suggested_format IS NOT NULL AND suggested_format = 'WebP')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_state",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(classification = 'AnimatedPng' AND frame_count IS NOT NULL AND frame_count > 1 AND uses_transparency IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'UsesTransparency' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS TRUE AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'WebpComparisonFailed' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS FALSE AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'OpaqueWebpCandidate' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS FALSE AND normalized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'LosslessWebp') OR (classification = 'OpaqueBelowWebpThreshold' AND frame_count IS NOT NULL AND frame_count = 1 AND uses_transparency IS FALSE AND normalized_png_bytes IS NOT NULL AND candidate_webp_bytes IS NOT NULL AND recommendation = 'None') OR (classification = 'NotPng' AND detected_format IS NOT NULL AND upper(detected_format) <> 'PNG' AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'FetchFailed' AND http_status_code IS NULL AND reason_code IS NOT NULL AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'HttpNonSuccess' AND http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299 AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification = 'ResponseTruncated' AND response_bytes > 0 AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None') OR (classification IN ('IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'DecodeFailed') AND width IS NULL AND normalized_png_bytes IS NULL AND recommendation = 'None')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_transparency",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "(uses_transparency IS NULL AND transparent_pixel_count IS NULL AND transparent_pixel_percent IS NULL) OR (uses_transparency IS TRUE AND transparent_pixel_count IS NOT NULL AND transparent_pixel_percent IS NOT NULL AND pixel_count IS NOT NULL AND transparent_pixel_count > 0 AND transparent_pixel_count <= pixel_count AND transparent_pixel_percent = round(transparent_pixel_count * 100.0 / pixel_count, 4)) OR (uses_transparency IS FALSE AND transparent_pixel_count IS NOT NULL AND transparent_pixel_percent IS NOT NULL AND transparent_pixel_count = 0 AND transparent_pixel_percent = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_image_result_transport",
                schema: "web_health",
                table: "png_audit_image_result",
                sql: "response_bytes >= 0 AND (http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599) AND (classification = 'FetchFailed' OR final_display_url IS NOT NULL) AND (classification <> 'HttpNonSuccess' OR (http_status_code IS NOT NULL AND http_status_code NOT BETWEEN 200 AND 299)) AND (classification NOT IN ('ResponseTruncated', 'NotPng', 'IdentificationFailed', 'UnsupportedBitDepth', 'DimensionsExceeded', 'PixelLimitExceeded', 'DecodedMemoryExceeded', 'AnimatedPng', 'DecodeFailed', 'UsesTransparency', 'WebpComparisonFailed', 'OpaqueWebpCandidate', 'OpaqueBelowWebpThreshold') OR (http_status_code IS NOT NULL AND http_status_code BETWEEN 200 AND 299))");
        }
    }
}

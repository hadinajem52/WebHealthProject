using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PngAuditPersistenceHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_run_counts",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_run_failure",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.AddColumn<byte[]>(
                name: "seed_url_identity_hash",
                schema: "web_health",
                table: "png_audit_run",
                type: "bytea",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE web_health.png_audit_run "
                + "SET seed_url_identity_hash = sha256(convert_to(seed_url_snapshot, 'UTF8'))");

            migrationBuilder.AlterColumn<byte[]>(
                name: "seed_url_identity_hash",
                schema: "web_health",
                table: "png_audit_run",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.Sql(
                "UPDATE web_health.png_audit_run SET "
                + "pages_discovered = LEAST(pages_discovered, max_pages), "
                + "images_discovered = LEAST(images_discovered, max_unique_images), "
                + "http_attempts = LEAST(http_attempts, max_total_http_attempts), "
                + "total_page_bytes = LEAST(total_page_bytes, max_total_page_bytes), "
                + "total_image_bytes = LEAST(total_image_bytes, max_total_image_bytes)");

            migrationBuilder.Sql(
                "UPDATE web_health.png_audit_run SET "
                + "images_analyzed = LEAST(images_analyzed, images_discovered), "
                + "recommendation_count = LEAST(recommendation_count, "
                + "LEAST(images_analyzed, images_discovered))");

            migrationBuilder.Sql(
                "UPDATE web_health.png_audit_run SET failure_code = CASE "
                + "WHEN status = 'Cancelled' THEN 'Cancelled' "
                + "WHEN status = 'Failed' AND failure_code NOT IN "
                + "('WorkerUnavailable', 'TargetChanged', 'TargetIneligible', "
                + "'AttemptsExhausted', 'StorageUnavailable', 'Unexpected') THEN 'Unexpected' "
                + "ELSE failure_code END");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_run_counts",
                schema: "web_health",
                table: "png_audit_run",
                sql: "attempt_count >= 0 AND pages_discovered >= 0 AND images_discovered >= 0 AND images_analyzed >= 0 AND recommendation_count >= 0 AND discovery_skip_count >= 0 AND http_attempts >= 0 AND total_page_bytes >= 0 AND total_image_bytes >= 0 AND images_analyzed <= images_discovered AND recommendation_count <= images_analyzed AND pages_discovered <= max_pages AND images_discovered <= max_unique_images AND http_attempts <= max_total_http_attempts AND total_page_bytes <= max_total_page_bytes AND total_image_bytes <= max_total_image_bytes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_run_failure",
                schema: "web_health",
                table: "png_audit_run",
                sql: "(status IN ('Failed', 'Cancelled')) = (failure_code IS NOT NULL) AND (failure_code IS NULL OR failure_code IN ('WorkerUnavailable', 'TargetChanged', 'TargetIneligible', 'AttemptsExhausted', 'StorageUnavailable', 'Cancelled', 'Unexpected')) AND (status <> 'Cancelled' OR failure_code = 'Cancelled') AND (status <> 'Failed' OR failure_code <> 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_run_seed_hash",
                schema: "web_health",
                table: "png_audit_run",
                sql: "octet_length(seed_url_identity_hash) = 32");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_run_counts",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_run_failure",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_run_seed_hash",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropColumn(
                name: "seed_url_identity_hash",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_run_counts",
                schema: "web_health",
                table: "png_audit_run",
                sql: "attempt_count >= 0 AND pages_discovered >= 0 AND images_discovered >= 0 AND images_analyzed >= 0 AND recommendation_count >= 0 AND discovery_skip_count >= 0 AND http_attempts >= 0 AND total_page_bytes >= 0 AND total_image_bytes >= 0 AND images_analyzed <= images_discovered AND recommendation_count <= images_analyzed");

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_run_failure",
                schema: "web_health",
                table: "png_audit_run",
                sql: "(status IN ('Failed', 'Cancelled')) = (failure_code IS NOT NULL)");
        }
    }
}

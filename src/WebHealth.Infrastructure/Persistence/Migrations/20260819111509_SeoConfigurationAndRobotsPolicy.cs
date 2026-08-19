using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeoConfigurationAndRobotsPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.AddColumn<bool>(
                name: "policy_description_required",
                schema: "web_health",
                table: "seo_observation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "policy_expected_host",
                schema: "web_health",
                table: "seo_observation",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_indexing_expectation",
                schema: "web_health",
                table: "seo_observation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "seo_description_required",
                schema: "web_health",
                table: "endpoint",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_expected_canonical_host",
                schema: "web_health",
                table: "endpoint",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_indexing_expectation",
                schema: "web_health",
                table: "endpoint",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.CreateTable(
                name: "robots_snapshot",
                schema: "web_health",
                columns: table => new
                {
                    origin = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    content = table.Column<string>(type: "character varying(524288)", maxLength: 524288, nullable: true),
                    sitemap_required = table.Column<bool>(type: "boolean", nullable: false),
                    configured_sitemap_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    checked_sitemap_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    sitemap_http_status = table.Column<int>(type: "integer", nullable: true),
                    sitemap_available = table.Column<bool>(type: "boolean", nullable: false),
                    exception_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    exception_approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exception_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_robots_snapshot", x => x.origin);
                    table.CheckConstraint("ck_robots_snapshot_content", "(status = 'Fetched') OR (content IS NULL)");
                    table.CheckConstraint("ck_robots_snapshot_exception_complete", "(exception_reason IS NULL AND exception_approved_by_user_id IS NULL AND exception_approved_at IS NULL) OR (exception_reason IS NOT NULL AND exception_approved_by_user_id IS NOT NULL AND exception_approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_robots_snapshot_expiry", "expires_at > fetched_at");
                    table.CheckConstraint("ck_robots_snapshot_port", "port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_robots_snapshot_status", "status IN ('Fetched', 'NotFound', 'Unavailable')");
                    table.ForeignKey(
                        name: "fk_robots_snapshot_app_user_exception_approved_by_user_id",
                        column: x => x.exception_approved_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_seo_expected_canonical_host",
                schema: "web_health",
                table: "endpoint",
                sql: "seo_expected_canonical_host IS NULL OR length(seo_expected_canonical_host) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_seo_indexing_expectation",
                schema: "web_health",
                table: "endpoint",
                sql: "seo_indexing_expectation IN ('Default', 'Indexable', 'NoIndex')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol','SlowResponse','PageTooLarge','SslExpired','SslNotYetValid','SslHostnameMismatch','SslUntrusted','SslHandshakeFailed','SslExpiringSoon','SeoTitle','SeoDescription','SeoCanonical','SeoIndexing','SeoRobots')");

            migrationBuilder.CreateIndex(
                name: "ix_robots_snapshot_exception_approved_by_user_id",
                schema: "web_health",
                table: "robots_snapshot",
                column: "exception_approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_robots_snapshot_expires_at",
                schema: "web_health",
                table: "robots_snapshot",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "robots_snapshot",
                schema: "web_health");

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_seo_expected_canonical_host",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_seo_indexing_expectation",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropColumn(
                name: "policy_description_required",
                schema: "web_health",
                table: "seo_observation");

            migrationBuilder.DropColumn(
                name: "policy_expected_host",
                schema: "web_health",
                table: "seo_observation");

            migrationBuilder.DropColumn(
                name: "policy_indexing_expectation",
                schema: "web_health",
                table: "seo_observation");

            migrationBuilder.DropColumn(
                name: "seo_description_required",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropColumn(
                name: "seo_expected_canonical_host",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropColumn(
                name: "seo_indexing_expectation",
                schema: "web_health",
                table: "endpoint");

            // Results recorded while the SEO categories existed have to be retired before the
            // constraint that outlaws them goes back on, or this migration cannot run down against
            // a database that used the feature. They are mapped rather than nulled: a Warning or
            // Critical result is required to carry a category, and ContentMismatch is the
            // pre-SEO category for a page whose content failed its expectation.
            migrationBuilder.Sql("""
                UPDATE web_health.check_result
                SET failure_category = 'ContentMismatch'
                WHERE failure_category IN
                    ('SeoTitle','SeoDescription','SeoCanonical','SeoIndexing','SeoRobots');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol','SlowResponse','PageTooLarge','SslExpired','SslNotYetValid','SslHostnameMismatch','SslUntrusted','SslHandshakeFailed','SslExpiringSoon')");
        }
    }
}

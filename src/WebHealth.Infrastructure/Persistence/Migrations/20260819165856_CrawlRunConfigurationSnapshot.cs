using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrawlRunConfigurationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allowed_hosts",
                schema: "web_health",
                table: "crawl_run",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allowed_path_prefixes",
                schema: "web_health",
                table: "crawl_run",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "check_external_links",
                schema: "web_health",
                table: "crawl_run",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "web_health",
                table: "crawl_run",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_depth",
                schema: "web_health",
                table: "crawl_run",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            // The generated default of 0 would violate ck_crawl_run_limits on any run this
            // migration's predecessor already allowed. The backfill is the shipped default, which
            // is what those runs were in fact executed with.
            migrationBuilder.AddColumn<int>(
                name: "max_pages",
                schema: "web_health",
                table: "crawl_run",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<string>(
                name: "query_policy",
                schema: "web_health",
                table: "crawl_run",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Canonicalize");

            migrationBuilder.AddColumn<int>(
                name: "duration_ms",
                schema: "web_health",
                table: "crawl_link_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_crawl_run_failure_reason",
                schema: "web_health",
                table: "crawl_run",
                sql: "(status = 'Failed') OR (failure_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crawl_run_limits",
                schema: "web_health",
                table: "crawl_run",
                sql: "max_pages > 0 AND max_depth >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crawl_run_query_policy",
                schema: "web_health",
                table: "crawl_run",
                sql: "query_policy IN ('Canonicalize', 'PreserveOrder', 'Ignore')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crawl_link_result_duration",
                schema: "web_health",
                table: "crawl_link_result",
                sql: "duration_ms IS NULL OR duration_ms >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crawl_run_failure_reason",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_crawl_run_limits",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_crawl_run_query_policy",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_crawl_link_result_duration",
                schema: "web_health",
                table: "crawl_link_result");

            migrationBuilder.DropColumn(
                name: "allowed_hosts",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "allowed_path_prefixes",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "check_external_links",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "max_depth",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "max_pages",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "query_policy",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                schema: "web_health",
                table: "crawl_link_result");
        }
    }
}

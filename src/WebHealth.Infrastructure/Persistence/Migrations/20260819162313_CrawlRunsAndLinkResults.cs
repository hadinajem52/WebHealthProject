using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrawlRunsAndLinkResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crawl_run",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stop_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    seed_urls = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    pages_fetched = table.Column<int>(type: "integer", nullable: false),
                    links_recorded = table.Column<int>(type: "integer", nullable: false),
                    robots_override_granted = table.Column<bool>(type: "boolean", nullable: false),
                    robots_override_refused_because = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_run", x => x.id);
                    table.CheckConstraint("ck_crawl_run_counts", "pages_fetched >= 0 AND links_recorded >= 0");
                    table.CheckConstraint("ck_crawl_run_finished_after_started", "finished_at IS NULL OR finished_at >= started_at");
                    table.CheckConstraint("ck_crawl_run_finished_when_terminal", "(status = 'Running') = (finished_at IS NULL)");
                    table.CheckConstraint("ck_crawl_run_override", "(robots_override_granted AND robots_override_refused_because IS NULL) OR (NOT robots_override_granted AND robots_override_refused_because IS NOT NULL)");
                    table.CheckConstraint("ck_crawl_run_status", "status IN ('Running', 'Completed', 'Cancelled', 'Failed')");
                    table.CheckConstraint("ck_crawl_run_status_stop_reason", "(status = 'Running') OR (status = 'Completed' AND stop_reason IN ('FrontierExhausted', 'PageLimit', 'DurationLimit')) OR (status = 'Cancelled' AND stop_reason = 'Cancelled') OR (status = 'Failed' AND stop_reason = 'Failed')");
                    table.CheckConstraint("ck_crawl_run_stop_reason", "stop_reason IN ('FrontierExhausted', 'PageLimit', 'DurationLimit', 'Cancelled', 'Failed')");
                    table.ForeignKey(
                        name: "fk_crawl_run_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crawl_link_result",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    source_url_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_url_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    redirect_count = table.Column<int>(type: "integer", nullable: false),
                    final_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_link_result", x => x.id);
                    table.CheckConstraint("ck_crawl_link_result_classification", "classification IN ('Healthy', 'Redirected', 'Broken', 'Blocked', 'Timeout', 'Skipped', 'Unknown')");
                    table.CheckConstraint("ck_crawl_link_result_depth", "depth >= -1");
                    table.CheckConstraint("ck_crawl_link_result_redirect_count", "redirect_count >= 0");
                    table.CheckConstraint("ck_crawl_link_result_skip_reason", "(classification IN ('Skipped', 'Unknown')) OR (skip_reason IS NULL)");
                    table.CheckConstraint("ck_crawl_link_result_source_hash", "(source_url IS NULL) = (source_url_hash IS NULL)");
                    table.CheckConstraint("ck_crawl_link_result_status_code", "status_code IS NULL OR status_code BETWEEN 100 AND 599");
                    table.ForeignKey(
                        name: "fk_crawl_link_result_crawl_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "web_health",
                        principalTable: "crawl_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crawl_link_result_run_classification",
                schema: "web_health",
                table: "crawl_link_result",
                columns: new[] { "run_id", "classification" });

            migrationBuilder.CreateIndex(
                name: "ux_crawl_link_result_pair",
                schema: "web_health",
                table: "crawl_link_result",
                columns: new[] { "run_id", "source_url_hash", "target_url_hash" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_crawl_run_endpoint_id_started_at",
                schema: "web_health",
                table: "crawl_run",
                columns: new[] { "endpoint_id", "started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crawl_link_result",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "crawl_run",
                schema: "web_health");
        }
    }
}

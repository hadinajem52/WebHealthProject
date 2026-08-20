using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageAuditFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "page_audit_target",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    strategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    scheduling_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    schedule_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_audit_target", x => x.id);
                    table.CheckConstraint("ck_page_audit_target_category", "category IN ('Seo')");
                    table.CheckConstraint("ck_page_audit_target_interval", "interval_seconds BETWEEN 21600 AND 2592000");
                    table.CheckConstraint("ck_page_audit_target_provider", "provider IN ('PageSpeedInsights')");
                    table.CheckConstraint("ck_page_audit_target_scheduling_requires_enabled", "is_enabled OR NOT scheduling_enabled");
                    table.CheckConstraint("ck_page_audit_target_strategy", "strategy IN ('Mobile', 'Desktop')");
                    table.CheckConstraint("ck_page_audit_target_updated_after_created", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_page_audit_target_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "page_audit_run",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_audit_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requested_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    final_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    raw_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    strategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lighthouse_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    warning_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    failure_category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    safe_diagnostic = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    analysis_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_audit_run", x => x.id);
                    table.CheckConstraint("ck_page_audit_run_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_page_audit_run_category", "category IN ('Seo')");
                    table.CheckConstraint("ck_page_audit_run_completed_contract", "status NOT IN ('Completed', 'CompletedWithWarnings') OR (raw_score IS NOT NULL AND failure_category IS NULL AND lighthouse_version IS NOT NULL)");
                    table.CheckConstraint("ck_page_audit_run_failure_category", "failure_category IS NULL OR failure_category IN ('ProviderRateLimited', 'ProviderUnavailable', 'ProviderTimeout', 'ProviderAuthenticationFailed', 'TargetRejected', 'CaptchaBlocked', 'LighthouseRuntimeError', 'ProviderContractInvalid', 'ProviderResponseTooLarge', 'ProviderResponseInvalid', 'Cancelled', 'UnknownProviderFailure')");
                    table.CheckConstraint("ck_page_audit_run_failure_contract", "status <> 'Failed' OR failure_category IS NOT NULL");
                    table.CheckConstraint("ck_page_audit_run_finished_after_queued", "finished_at IS NULL OR finished_at >= queued_at");
                    table.CheckConstraint("ck_page_audit_run_finished_when_terminal", "(status IN ('Queued', 'Running')) = (finished_at IS NULL)");
                    table.CheckConstraint("ck_page_audit_run_lease_pair", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_page_audit_run_provider", "provider IN ('PageSpeedInsights')");
                    table.CheckConstraint("ck_page_audit_run_raw_score", "raw_score IS NULL OR raw_score BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_page_audit_run_source", "source IN ('Scheduled', 'Manual')");
                    table.CheckConstraint("ck_page_audit_run_status", "status IN ('Queued', 'Running', 'Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled')");
                    table.CheckConstraint("ck_page_audit_run_strategy", "strategy IN ('Mobile', 'Desktop')");
                    table.CheckConstraint("ck_page_audit_run_terminal_has_no_lease", "status IN ('Queued', 'Running') OR lease_token IS NULL");
                    table.ForeignKey(
                        name: "fk_page_audit_run_page_audit_target_page_audit_target_id",
                        column: x => x.page_audit_target_id,
                        principalSchema: "web_health",
                        principalTable: "page_audit_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "page_audit_item",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    score_display_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    weight = table.Column<double>(type: "double precision", nullable: false),
                    group_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    display_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_audit_item", x => x.id);
                    table.CheckConstraint("ck_page_audit_item_score", "score IS NULL OR score BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_page_audit_item_scored_statuses_have_a_score", "status NOT IN ('Passed', 'Failed', 'Scored') OR score IS NOT NULL");
                    table.CheckConstraint("ck_page_audit_item_status", "status IN ('Passed', 'Failed', 'Scored', 'Manual', 'NotApplicable', 'Informative', 'Error')");
                    table.CheckConstraint("ck_page_audit_item_weight", "weight >= 0");
                    table.ForeignKey(
                        name: "fk_page_audit_item_page_audit_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "web_health",
                        principalTable: "page_audit_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_item_run_status",
                schema: "web_health",
                table: "page_audit_item",
                columns: new[] { "run_id", "status", "audit_id" });

            migrationBuilder.CreateIndex(
                name: "ux_page_audit_item_run_audit",
                schema: "web_health",
                table: "page_audit_item",
                columns: new[] { "run_id", "audit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_run_endpoint_finished",
                schema: "web_health",
                table: "page_audit_run",
                columns: new[] { "endpoint_id", "finished_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_run_status_updated",
                schema: "web_health",
                table: "page_audit_run",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_run_target_finished",
                schema: "web_health",
                table: "page_audit_run",
                columns: new[] { "page_audit_target_id", "finished_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_page_audit_run_active",
                schema: "web_health",
                table: "page_audit_run",
                column: "page_audit_target_id",
                unique: true,
                filter: "status IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_target_due",
                schema: "web_health",
                table: "page_audit_target",
                columns: new[] { "next_due_at", "id" },
                filter: "is_enabled AND scheduling_enabled");

            migrationBuilder.CreateIndex(
                name: "ux_page_audit_target_profile",
                schema: "web_health",
                table: "page_audit_target",
                columns: new[] { "endpoint_id", "provider", "category", "strategy" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_audit_item",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "page_audit_run",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "page_audit_target",
                schema: "web_health");
        }
    }
}

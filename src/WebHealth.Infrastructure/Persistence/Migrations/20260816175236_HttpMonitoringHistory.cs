using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HttpMonitoringHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accepted_status_codes",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "content_marker_comparison",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OrdinalIgnoreCase");

            migrationBuilder.AddColumn<int>(
                name: "max_redirects",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "max_response_body_bytes",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: false,
                defaultValue: 2097152);

            migrationBuilder.AddColumn<string>(
                name: "production_http_severity",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Warning");

            migrationBuilder.AddColumn<string>(
                name: "required_content_marker",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "check_result",
                schema: "web_health",
                columns: table => new
                {
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    dns_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    connect_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    tls_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    ttfb_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    total_duration_ms = table.Column<int>(type: "integer", nullable: false),
                    transferred_length = table.Column<long>(type: "bigint", nullable: true),
                    decoded_length = table.Column<long>(type: "bigint", nullable: true),
                    length_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    response_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    monitor_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    counts_for_uptime = table.Column<bool>(type: "boolean", nullable: false),
                    safe_diagnostic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_result", x => x.logical_check_id);
                    table.CheckConstraint("ck_check_result_completed", "completed_at >= measured_at");
                    table.CheckConstraint("ck_check_result_failure_category", "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','Protocol')");
                    table.CheckConstraint("ck_check_result_http_status", "http_status IS NULL OR http_status BETWEEN 100 AND 599");
                    table.CheckConstraint("ck_check_result_lengths", "(transferred_length IS NULL OR transferred_length >= 0) AND (decoded_length IS NULL OR decoded_length >= 0) AND ((decoded_length IS NULL AND length_source IS NULL) OR (decoded_length IS NOT NULL AND length_source IS NOT NULL))");
                    table.CheckConstraint("ck_check_result_more_timings", "(connect_duration_ms IS NULL OR connect_duration_ms >= 0) AND (tls_duration_ms IS NULL OR tls_duration_ms >= 0) AND (ttfb_duration_ms IS NULL OR ttfb_duration_ms >= 0) AND total_duration_ms >= 0");
                    table.CheckConstraint("ck_check_result_outcome", "outcome IN ('Healthy', 'Warning', 'Critical', 'Cancelled')");
                    table.CheckConstraint("ck_check_result_outcome_category", "(outcome = 'Healthy' AND failure_category IS NULL) OR (outcome = 'Cancelled' AND failure_category = 'Cancellation') OR (outcome IN ('Warning','Critical') AND failure_category IS NOT NULL)");
                    table.CheckConstraint("ck_check_result_timings", "dns_duration_ms IS NULL OR dns_duration_ms >= 0");
                    table.CheckConstraint("ck_check_result_truncation", "NOT response_truncated OR failure_category = 'ResponseTooLarge'");
                    table.ForeignKey(
                        name: "fk_check_result_logical_check_logical_check_id",
                        column: x => x.logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finding",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observed_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expected_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    issue_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finding", x => x.id);
                    table.CheckConstraint("ck_finding_severity", "severity IN ('Warning', 'Critical')");
                    table.ForeignKey(
                        name: "fk_finding_check_result_logical_check_id",
                        column: x => x.logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "check_result",
                        principalColumn: "logical_check_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "redirect_hop",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hop_number = table.Column<int>(type: "integer", nullable: false),
                    normalized_from_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_to_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: false),
                    is_loop = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redirect_hop", x => x.id);
                    table.CheckConstraint("ck_redirect_hop_number", "hop_number > 0");
                    table.CheckConstraint("ck_redirect_hop_status", "http_status BETWEEN 300 AND 399");
                    table.ForeignKey(
                        name: "fk_redirect_hop_check_result_logical_check_id",
                        column: x => x.logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "check_result",
                        principalColumn: "logical_check_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_accepted_statuses",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "accepted_status_codes = '' OR accepted_status_codes ~ '^[1-5][0-9]{2}(,[1-5][0-9]{2})*$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_http_limits",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "max_response_body_bytes BETWEEN 1 AND 2097152 AND max_redirects BETWEEN 0 AND 10");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_http_severity",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "production_http_severity IN ('Warning', 'Critical')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_marker_comparison",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "content_marker_comparison IN ('Ordinal', 'OrdinalIgnoreCase')");

            migrationBuilder.CreateIndex(
                name: "ix_check_result_measured_at_logical_check_id",
                schema: "web_health",
                table: "check_result",
                columns: new[] { "measured_at", "logical_check_id" });

            migrationBuilder.CreateIndex(
                name: "ix_finding_logical_check_id_issue_key_rule_key",
                schema: "web_health",
                table: "finding",
                columns: new[] { "logical_check_id", "issue_key", "rule_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redirect_hop_logical_check_id_hop_number",
                schema: "web_health",
                table: "redirect_hop",
                columns: new[] { "logical_check_id", "hop_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finding",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "redirect_hop",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "check_result",
                schema: "web_health");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_accepted_statuses",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_http_limits",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_http_severity",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_marker_comparison",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "accepted_status_codes",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "content_marker_comparison",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "max_redirects",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "max_response_body_bytes",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "production_http_severity",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "required_content_marker",
                schema: "web_health",
                table: "check_configuration_snapshot");
        }
    }
}

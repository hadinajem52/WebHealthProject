using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageAuditIncidentPoliciesAndBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_evidence_source",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                schema: "web_health",
                table: "page_audit_run",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string>(
                name: "numeric_unit",
                schema: "web_health",
                table: "page_audit_item",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "numeric_value",
                schema: "web_health",
                table: "page_audit_item",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "page_audit_run_id",
                schema: "web_health",
                table: "incident_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "page_audit_incident_policy",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incidents_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    performance_score_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    performance_minimum_score = table.Column<int>(type: "integer", nullable: false),
                    accessibility_score_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    accessibility_minimum_score = table.Column<int>(type: "integer", nullable: false),
                    best_practices_score_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    best_practices_minimum_score = table.Column<int>(type: "integer", nullable: false),
                    seo_score_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    seo_minimum_score = table.Column<int>(type: "integer", nullable: false),
                    first_contentful_paint_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    first_contentful_paint_maximum = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    largest_contentful_paint_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    largest_contentful_paint_maximum = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    total_blocking_time_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    total_blocking_time_maximum = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    cumulative_layout_shift_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    cumulative_layout_shift_maximum = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    speed_index_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    speed_index_maximum = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_audit_incident_policy", x => x.id);
                    table.CheckConstraint("ck_page_audit_incident_policy_metric_limits", "first_contentful_paint_maximum BETWEEN 0 AND 600000 AND largest_contentful_paint_maximum BETWEEN 0 AND 600000 AND total_blocking_time_maximum BETWEEN 0 AND 600000 AND cumulative_layout_shift_maximum BETWEEN 0 AND 10 AND speed_index_maximum BETWEEN 0 AND 600000");
                    table.CheckConstraint("ck_page_audit_incident_policy_scores", "performance_minimum_score BETWEEN 0 AND 100 AND accessibility_minimum_score BETWEEN 0 AND 100 AND best_practices_minimum_score BETWEEN 0 AND 100 AND seo_minimum_score BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_page_audit_incident_policy_singleton", "id = '58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0'::uuid");
                    table.ForeignKey(
                        name: "fk_page_audit_incident_policy_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "web_health",
                table: "page_audit_incident_policy",
                columns: new[] { "id", "accessibility_minimum_score", "accessibility_score_enabled", "best_practices_minimum_score", "best_practices_score_enabled", "cumulative_layout_shift_enabled", "cumulative_layout_shift_maximum", "first_contentful_paint_enabled", "first_contentful_paint_maximum", "incidents_enabled", "largest_contentful_paint_enabled", "largest_contentful_paint_maximum", "performance_minimum_score", "performance_score_enabled", "seo_minimum_score", "seo_score_enabled", "speed_index_enabled", "speed_index_maximum", "total_blocking_time_enabled", "total_blocking_time_maximum", "updated_at", "updated_by_user_id", "version" },
                values: new object[] { new Guid("58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0"), 90, true, 90, true, false, 0.1m, false, 1800m, false, false, 2500m, 90, true, 90, true, false, 3400m, false, 200m, new DateTimeOffset(new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L });

            migrationBuilder.InsertData(
                schema: "web_health",
                table: "policy_profile",
                columns: new[] { "id", "bounded_settings", "created_at", "deleted_at", "is_system", "monitor_type", "name", "version" },
                values: new object[] { new Guid("624bbbda-96d1-46f8-8382-16686d3f400e"), "{}", new DateTimeOffset(new DateTime(2026, 8, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "PageSpeedInsights", "Default PageSpeed incidents", 1L });

            migrationBuilder.Sql(
                """
                INSERT INTO web_health.endpoint_monitor (
                    id, endpoint_id, policy_profile_id, monitor_type, bounded_overrides,
                    schedule_anchor, next_due_at, configuration_fingerprint,
                    interval_seconds, timeout_seconds,
                    failure_confirmation_count, recovery_confirmation_count,
                    warning_threshold_ms, critical_threshold_ms,
                    scheduling_enabled, is_enabled,
                    created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                SELECT
                    gen_random_uuid(),
                    endpoint.id,
                    '624bbbda-96d1-46f8-8382-16686d3f400e'::uuid,
                    'PageSpeedInsights',
                    '{}'::jsonb,
                    now(),
                    now() + interval '1 day',
                    encode(sha256(convert_to(
                        'v2|'
                        || octet_length(endpoint.normalized_url)::text || ':'
                        || endpoint.normalized_url || '|'
                        || '17:PageSpeedInsights|'
                        || '1:' || CASE WHEN environment.is_production THEN '1' ELSE '0' END || '|'
                        || '5:86400|2:90|1:1|1:1|-1:|-1:|0:|-1:|'
                        || '17:OrdinalIgnoreCase|7:Warning|7:2097152|2:10|',
                        'UTF8')), 'hex'),
                    86400, 90, 1, 1, NULL, NULL,
                    false, true,
                    now(), http_monitor.created_by_user_id, now(), http_monitor.updated_by_user_id, 1
                FROM web_health.endpoint AS endpoint
                JOIN web_health.environment AS environment ON environment.id = endpoint.environment_id
                JOIN web_health.endpoint_monitor AS http_monitor
                  ON http_monitor.endpoint_id = endpoint.id
                 AND http_monitor.monitor_type = 'HttpAvailability'
                 AND http_monitor.deleted_at IS NULL
                WHERE endpoint.deleted_at IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM web_health.page_audit_target AS target
                      WHERE target.endpoint_id = endpoint.id
                        AND target.provider = 'PageSpeedInsights'
                        AND target.is_enabled)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM web_health.endpoint_monitor AS existing
                      WHERE existing.endpoint_id = endpoint.id
                        AND existing.monitor_type = 'PageSpeedInsights'
                        AND existing.deleted_at IS NULL);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_run_batch_strategy",
                schema: "web_health",
                table: "page_audit_run",
                columns: new[] { "batch_id", "strategy" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_item_numeric_value",
                schema: "web_health",
                table: "page_audit_item",
                sql: "numeric_value IS NULL OR numeric_value >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_page_audit_run_id",
                schema: "web_health",
                table: "incident_evidence",
                column: "page_audit_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_evidence_source",
                schema: "web_health",
                table: "incident_evidence",
                sql: "(evidence_type IN ('Opening', 'Failure', 'Recovery') AND (logical_check_id IS NOT NULL)::int + (page_audit_run_id IS NOT NULL)::int = 1 AND actor_user_id IS NULL) OR (evidence_type = 'Resolution' AND (((logical_check_id IS NOT NULL)::int + (page_audit_run_id IS NOT NULL)::int = 1 AND actor_user_id IS NULL) OR (logical_check_id IS NULL AND page_audit_run_id IS NULL AND actor_user_id IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_incident_policy_updated_by_user_id",
                schema: "web_health",
                table: "page_audit_incident_policy",
                column: "updated_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_incident_evidence_page_audit_run_page_audit_run_id",
                schema: "web_health",
                table: "incident_evidence",
                column: "page_audit_run_id",
                principalSchema: "web_health",
                principalTable: "page_audit_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM web_health.notification_attempt
                WHERE notification_delivery_id IN (
                    SELECT delivery.id
                    FROM web_health.notification_delivery AS delivery
                    JOIN web_health.notification_event AS notification
                      ON notification.id = delivery.notification_event_id
                    JOIN web_health.incident AS incident ON incident.id = notification.incident_id
                    JOIN web_health.endpoint_monitor AS monitor
                      ON monitor.id = incident.endpoint_monitor_id
                    WHERE monitor.monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.notification_delivery
                WHERE notification_event_id IN (
                    SELECT notification.id
                    FROM web_health.notification_event AS notification
                    JOIN web_health.incident AS incident ON incident.id = notification.incident_id
                    JOIN web_health.endpoint_monitor AS monitor
                      ON monitor.id = incident.endpoint_monitor_id
                    WHERE monitor.monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.notification_event
                WHERE incident_id IN (
                    SELECT incident.id
                    FROM web_health.incident AS incident
                    JOIN web_health.endpoint_monitor AS monitor
                      ON monitor.id = incident.endpoint_monitor_id
                    WHERE monitor.monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.incident_evidence
                WHERE endpoint_monitor_id IN (
                    SELECT id FROM web_health.endpoint_monitor
                    WHERE monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.incident_event
                WHERE incident_id IN (
                    SELECT incident.id
                    FROM web_health.incident AS incident
                    JOIN web_health.endpoint_monitor AS monitor
                      ON monitor.id = incident.endpoint_monitor_id
                    WHERE monitor.monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.incident
                WHERE endpoint_monitor_id IN (
                    SELECT id FROM web_health.endpoint_monitor
                    WHERE monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.issue_state
                WHERE endpoint_monitor_id IN (
                    SELECT id FROM web_health.endpoint_monitor
                    WHERE monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.maintenance_target
                WHERE endpoint_monitor_id IN (
                    SELECT id FROM web_health.endpoint_monitor
                    WHERE monitor_type = 'PageSpeedInsights');

                DELETE FROM web_health.endpoint_monitor
                WHERE monitor_type = 'PageSpeedInsights';
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_incident_evidence_page_audit_run_page_audit_run_id",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropTable(
                name: "page_audit_incident_policy",
                schema: "web_health");

            migrationBuilder.DropIndex(
                name: "ix_page_audit_run_batch_strategy",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_item_numeric_value",
                schema: "web_health",
                table: "page_audit_item");

            migrationBuilder.DropIndex(
                name: "ix_incident_evidence_page_audit_run_id",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_evidence_source",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DeleteData(
                schema: "web_health",
                table: "policy_profile",
                keyColumn: "id",
                keyValue: new Guid("624bbbda-96d1-46f8-8382-16686d3f400e"));

            migrationBuilder.DropColumn(
                name: "batch_id",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.DropColumn(
                name: "numeric_unit",
                schema: "web_health",
                table: "page_audit_item");

            migrationBuilder.DropColumn(
                name: "numeric_value",
                schema: "web_health",
                table: "page_audit_item");

            migrationBuilder.DropColumn(
                name: "page_audit_run_id",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_evidence_source",
                schema: "web_health",
                table: "incident_evidence",
                sql: "(evidence_type IN ('Opening', 'Failure', 'Recovery') AND logical_check_id IS NOT NULL AND actor_user_id IS NULL) OR (evidence_type = 'Resolution' AND ((logical_check_id IS NOT NULL AND actor_user_id IS NULL) OR (logical_check_id IS NULL AND actor_user_id IS NOT NULL)))");
        }
    }
}

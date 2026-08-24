using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageAuditEndpointIncidentPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_incident_policy_singleton",
                schema: "web_health",
                table: "page_audit_incident_policy");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "web_health",
                table: "page_audit_incident_policy",
                newName: "endpoint_id");

            migrationBuilder.Sql(
                """
                INSERT INTO web_health.page_audit_incident_policy (
                    endpoint_id, incidents_enabled,
                    performance_score_enabled, performance_minimum_score,
                    accessibility_score_enabled, accessibility_minimum_score,
                    best_practices_score_enabled, best_practices_minimum_score,
                    seo_score_enabled, seo_minimum_score,
                    first_contentful_paint_enabled, first_contentful_paint_maximum,
                    largest_contentful_paint_enabled, largest_contentful_paint_maximum,
                    total_blocking_time_enabled, total_blocking_time_maximum,
                    cumulative_layout_shift_enabled, cumulative_layout_shift_maximum,
                    speed_index_enabled, speed_index_maximum,
                    updated_at, updated_by_user_id, version)
                SELECT DISTINCT
                    target.endpoint_id, policy.incidents_enabled,
                    policy.performance_score_enabled, policy.performance_minimum_score,
                    policy.accessibility_score_enabled, policy.accessibility_minimum_score,
                    policy.best_practices_score_enabled, policy.best_practices_minimum_score,
                    policy.seo_score_enabled, policy.seo_minimum_score,
                    policy.first_contentful_paint_enabled, policy.first_contentful_paint_maximum,
                    policy.largest_contentful_paint_enabled, policy.largest_contentful_paint_maximum,
                    policy.total_blocking_time_enabled, policy.total_blocking_time_maximum,
                    policy.cumulative_layout_shift_enabled, policy.cumulative_layout_shift_maximum,
                    policy.speed_index_enabled, policy.speed_index_maximum,
                    policy.updated_at, policy.updated_by_user_id, policy.version
                FROM web_health.page_audit_target AS target
                CROSS JOIN web_health.page_audit_incident_policy AS policy
                WHERE target.provider = 'PageSpeedInsights'
                  AND policy.endpoint_id = '58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0'::uuid
                ON CONFLICT (endpoint_id) DO NOTHING;

                DELETE FROM web_health.page_audit_incident_policy AS policy
                WHERE policy.endpoint_id = '58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0'::uuid
                  AND NOT EXISTS (
                      SELECT 1
                      FROM web_health.page_audit_target AS target
                      WHERE target.endpoint_id = policy.endpoint_id
                        AND target.provider = 'PageSpeedInsights');
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_page_audit_incident_policy_endpoint_endpoint_id",
                schema: "web_health",
                table: "page_audit_incident_policy",
                column: "endpoint_id",
                principalSchema: "web_health",
                principalTable: "endpoint",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_page_audit_incident_policy_endpoint_endpoint_id",
                schema: "web_health",
                table: "page_audit_incident_policy");

            migrationBuilder.Sql(
                """
                WITH selected AS MATERIALIZED (
                    SELECT *
                    FROM web_health.page_audit_incident_policy
                    ORDER BY updated_at DESC, endpoint_id
                    LIMIT 1
                ), removed AS (
                    DELETE FROM web_health.page_audit_incident_policy
                )
                INSERT INTO web_health.page_audit_incident_policy (
                    endpoint_id, incidents_enabled,
                    performance_score_enabled, performance_minimum_score,
                    accessibility_score_enabled, accessibility_minimum_score,
                    best_practices_score_enabled, best_practices_minimum_score,
                    seo_score_enabled, seo_minimum_score,
                    first_contentful_paint_enabled, first_contentful_paint_maximum,
                    largest_contentful_paint_enabled, largest_contentful_paint_maximum,
                    total_blocking_time_enabled, total_blocking_time_maximum,
                    cumulative_layout_shift_enabled, cumulative_layout_shift_maximum,
                    speed_index_enabled, speed_index_maximum,
                    updated_at, updated_by_user_id, version)
                SELECT
                    '58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0'::uuid, incidents_enabled,
                    performance_score_enabled, performance_minimum_score,
                    accessibility_score_enabled, accessibility_minimum_score,
                    best_practices_score_enabled, best_practices_minimum_score,
                    seo_score_enabled, seo_minimum_score,
                    first_contentful_paint_enabled, first_contentful_paint_maximum,
                    largest_contentful_paint_enabled, largest_contentful_paint_maximum,
                    total_blocking_time_enabled, total_blocking_time_maximum,
                    cumulative_layout_shift_enabled, cumulative_layout_shift_maximum,
                    speed_index_enabled, speed_index_maximum,
                    updated_at, updated_by_user_id, version
                FROM selected
                UNION ALL
                SELECT
                    '58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0'::uuid, false,
                    true, 90, true, 90, true, 90, true, 90,
                    false, 1800, false, 2500, false, 200, false, 0.1, false, 3400,
                    '2026-08-24 00:00:00+00'::timestamptz, NULL, 1
                WHERE NOT EXISTS (SELECT 1 FROM selected);
                """);

            migrationBuilder.RenameColumn(
                name: "endpoint_id",
                schema: "web_health",
                table: "page_audit_incident_policy",
                newName: "id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_incident_policy_singleton",
                schema: "web_health",
                table: "page_audit_incident_policy",
                sql: "id = '58af6bcc-d2e8-4e8c-9d16-51a2a35df9f0'::uuid");
        }
    }
}

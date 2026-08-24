using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageAuditAllCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_target_category",
                schema: "web_health",
                table: "page_audit_target");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_run_category",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_target_category",
                schema: "web_health",
                table: "page_audit_target",
                sql: "category IN ('Performance', 'Accessibility', 'BestPractices', 'Seo')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_run_category",
                schema: "web_health",
                table: "page_audit_run",
                sql: "category IN ('Performance', 'Accessibility', 'BestPractices', 'Seo')");

            migrationBuilder.Sql(
                """
                INSERT INTO web_health.page_audit_target (
                    id, endpoint_id, provider, category, strategy, is_enabled, scheduling_enabled,
                    interval_seconds, schedule_anchor, next_due_at, created_at, updated_at, version)
                SELECT
                    gen_random_uuid(),
                    source.endpoint_id,
                    source.provider,
                    category.name,
                    source.strategy,
                    source.is_enabled,
                    source.scheduling_enabled,
                    source.interval_seconds,
                    source.schedule_anchor,
                    source.next_due_at,
                    now(),
                    now(),
                    1
                FROM web_health.page_audit_target AS source
                CROSS JOIN (
                    VALUES ('Performance'), ('Accessibility'), ('BestPractices')
                ) AS category(name)
                WHERE source.category = 'Seo'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM web_health.page_audit_target AS existing
                      WHERE existing.endpoint_id = source.endpoint_id
                        AND existing.provider = source.provider
                        AND existing.category = category.name
                        AND existing.strategy = source.strategy);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM web_health.page_audit_item AS item
                USING web_health.page_audit_run AS run
                WHERE item.run_id = run.id
                  AND run.category <> 'Seo';
                """);

            migrationBuilder.Sql(
                "DELETE FROM web_health.page_audit_run WHERE category <> 'Seo';");

            migrationBuilder.Sql(
                "DELETE FROM web_health.page_audit_target WHERE category <> 'Seo';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_target_category",
                schema: "web_health",
                table: "page_audit_target");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_run_category",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_target_category",
                schema: "web_health",
                table: "page_audit_target",
                sql: "category IN ('Seo')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_run_category",
                schema: "web_health",
                table: "page_audit_run",
                sql: "category IN ('Seo')");
        }
    }
}

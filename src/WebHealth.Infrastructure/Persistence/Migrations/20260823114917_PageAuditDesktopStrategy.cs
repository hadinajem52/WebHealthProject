using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives every endpoint already audited on mobile a desktop target as well.
    /// </summary>
    /// <remarks>
    /// Data only: the schema has allowed both strategies since the feature was added, and the
    /// unique profile index already keeps one row per form factor. Without this an endpoint
    /// configured before desktop auditing existed would show a mobile score and an empty desktop
    /// tab until somebody happened to open its form and save it again.
    /// </remarks>
    public partial class PageAuditDesktopStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The desktop row inherits the mobile row's cadence and anchor rather than becoming
            // due immediately, so a backfill over many endpoints does not spend a day's quota the
            // moment it is applied. Its own timestamps are the row's, because it is created now.
            migrationBuilder.Sql(
                """
                INSERT INTO web_health.page_audit_target (
                    id, endpoint_id, provider, category, strategy, is_enabled, scheduling_enabled,
                    interval_seconds, schedule_anchor, next_due_at, created_at, updated_at, version)
                SELECT
                    gen_random_uuid(),
                    mobile.endpoint_id,
                    mobile.provider,
                    mobile.category,
                    'Desktop',
                    mobile.is_enabled,
                    mobile.scheduling_enabled,
                    mobile.interval_seconds,
                    mobile.schedule_anchor,
                    mobile.next_due_at,
                    now(),
                    now(),
                    1
                FROM web_health.page_audit_target AS mobile
                WHERE mobile.strategy = 'Mobile'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM web_health.page_audit_target AS desktop
                      WHERE desktop.endpoint_id = mobile.endpoint_id
                        AND desktop.provider = mobile.provider
                        AND desktop.category = mobile.category
                        AND desktop.strategy = 'Desktop');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back is going back to a mobile-only world, so the desktop runs this migration
            // made possible are retired before the targets they hang off. Both foreign keys are
            // Restrict, so deleting the targets alone would fail against exactly the data Up
            // allowed to accumulate.
            migrationBuilder.Sql(
                """
                DELETE FROM web_health.page_audit_item AS item
                USING web_health.page_audit_run AS run,
                      web_health.page_audit_target AS target
                WHERE item.run_id = run.id
                  AND run.page_audit_target_id = target.id
                  AND target.strategy = 'Desktop';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM web_health.page_audit_run AS run
                USING web_health.page_audit_target AS target
                WHERE run.page_audit_target_id = target.id
                  AND target.strategy = 'Desktop';
                """);

            migrationBuilder.Sql(
                "DELETE FROM web_health.page_audit_target WHERE strategy = 'Desktop';");
        }
    }
}

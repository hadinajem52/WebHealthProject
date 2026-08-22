using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrawlRunActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A unique index cannot be created over data that already violates it. Until this
            // migration nothing in the application enqueued a crawl, so any row still sitting in
            // Running belongs to no worker; where an endpoint has more than one, the older ones
            // are retired so the newest keeps the slot. Retiring rather than deleting: a run that
            // was opened is a fact, and its links are already recorded against it.
            migrationBuilder.Sql("""
                UPDATE web_health.crawl_run AS stale
                SET status = 'Failed',
                    stop_reason = 'Failed',
                    failure_reason = 'Retired when the active-crawl uniqueness constraint was introduced.',
                    finished_at = COALESCE(finished_at, now())
                WHERE status = 'Running'
                  AND EXISTS (
                      SELECT 1
                      FROM web_health.crawl_run AS newer
                      WHERE newer.endpoint_id = stale.endpoint_id
                        AND newer.status = 'Running'
                        AND (newer.started_at, newer.id) > (stale.started_at, stale.id));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_crawl_run_active",
                schema: "web_health",
                table: "crawl_run",
                column: "endpoint_id",
                unique: true,
                filter: "status = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_crawl_run_active",
                schema: "web_health",
                table: "crawl_run");
        }
    }
}

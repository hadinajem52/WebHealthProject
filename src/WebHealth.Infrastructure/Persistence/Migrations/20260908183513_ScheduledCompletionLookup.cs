using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class ScheduledCompletionLookup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_logical_check_monitor_scheduled_completion",
                schema: "web_health",
                table: "logical_check",
                columns: new[] { "endpoint_monitor_id", "completed_at", "id" },
                descending: new[] { false, true, true },
                filter: "source = 'Scheduled' AND state = 'Completed' AND completed_at IS NOT NULL");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_logical_check_monitor_scheduled_completion",
                schema: "web_health",
                table: "logical_check");
        }
    }
}

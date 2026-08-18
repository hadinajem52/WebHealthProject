using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EndpointSchedulingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_endpoint_monitor_next_due_at_id",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.AddColumn<bool>(
                name: "scheduling_enabled",
                schema: "web_health",
                table: "endpoint_monitor",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_next_due_at_id",
                schema: "web_health",
                table: "endpoint_monitor",
                columns: new[] { "next_due_at", "id" },
                filter: "deleted_at IS NULL AND is_enabled AND scheduling_enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_endpoint_monitor_next_due_at_id",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.DropColumn(
                name: "scheduling_enabled",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_next_due_at_id",
                schema: "web_health",
                table: "endpoint_monitor",
                columns: new[] { "next_due_at", "id" },
                filter: "deleted_at IS NULL AND is_enabled");
        }
    }
}

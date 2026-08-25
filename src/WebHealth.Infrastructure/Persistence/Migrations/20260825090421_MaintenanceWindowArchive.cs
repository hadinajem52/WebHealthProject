using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceWindowArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "web_health",
                table: "maintenance_window",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_window_archived_at",
                schema: "web_health",
                table: "maintenance_window",
                column: "archived_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_maintenance_window_archived_at",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "web_health",
                table: "maintenance_window");
        }
    }
}

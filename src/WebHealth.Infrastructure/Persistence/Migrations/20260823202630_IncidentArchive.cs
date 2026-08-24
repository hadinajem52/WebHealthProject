using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "web_health",
                table: "incident",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_incident_archived_at",
                schema: "web_health",
                table: "incident",
                column: "archived_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_archived_status",
                schema: "web_health",
                table: "incident",
                sql: "archived_at IS NULL OR status IN ('Resolved', 'Closed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_incident_archived_at",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_archived_status",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "web_health",
                table: "incident");
        }
    }
}

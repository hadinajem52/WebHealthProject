using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportingSampleMonitorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_check_result_logical_check_logical_check_id",
                schema: "web_health",
                table: "check_result");

            // Added nullable and backfilled from the check each sample already belongs to, then
            // tightened. Adding it NOT NULL with a placeholder default would write a monitor
            // identity that is not any monitor, and the composite foreign key below would then
            // refuse every existing row.
            migrationBuilder.AddColumn<Guid>(
                name: "endpoint_monitor_id",
                schema: "web_health",
                table: "check_result",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE web_health.check_result AS result
                SET endpoint_monitor_id = check_row.endpoint_monitor_id
                FROM web_health.logical_check AS check_row
                WHERE check_row.id = result.logical_check_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "endpoint_monitor_id",
                schema: "web_health",
                table: "check_result",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_check_result_logical_check_id_endpoint_monitor_id",
                schema: "web_health",
                table: "check_result",
                columns: new[] { "logical_check_id", "endpoint_monitor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_check_result_monitor_measured_at",
                schema: "web_health",
                table: "check_result",
                columns: new[] { "endpoint_monitor_id", "measured_at" })
                .Annotation("Npgsql:IndexInclude", new[] { "outcome", "counts_for_uptime", "total_duration_ms", "monitor_source" });

            migrationBuilder.AddForeignKey(
                name: "fk_check_result_logical_check_monitor",
                schema: "web_health",
                table: "check_result",
                columns: new[] { "logical_check_id", "endpoint_monitor_id" },
                principalSchema: "web_health",
                principalTable: "logical_check",
                principalColumns: new[] { "id", "endpoint_monitor_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_check_result_logical_check_monitor",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropIndex(
                name: "ix_check_result_logical_check_id_endpoint_monitor_id",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropIndex(
                name: "ix_check_result_monitor_measured_at",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropColumn(
                name: "endpoint_monitor_id",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.AddForeignKey(
                name: "fk_check_result_logical_check_logical_check_id",
                schema: "web_health",
                table: "check_result",
                column: "logical_check_id",
                principalSchema: "web_health",
                principalTable: "logical_check",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

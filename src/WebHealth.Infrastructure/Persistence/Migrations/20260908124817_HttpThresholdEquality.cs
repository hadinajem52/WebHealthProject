using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class HttpThresholdEquality : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_monitor_threshold_order",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_threshold_order",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_monitor_threshold_order",
                schema: "web_health",
                table: "endpoint_monitor",
                sql: "(warning_threshold_ms IS NULL OR warning_threshold_ms >= 0) AND (critical_threshold_ms IS NULL OR critical_threshold_ms >= 0) AND (warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms <= critical_threshold_ms)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_threshold_order",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "(warning_threshold_ms IS NULL OR warning_threshold_ms >= 0) AND (critical_threshold_ms IS NULL OR critical_threshold_ms >= 0) AND (warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms <= critical_threshold_ms)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

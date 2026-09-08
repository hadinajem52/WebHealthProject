using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class MonitoringRuntimeState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_runtime_state",
                schema: "web_health",
                columns: table => new
                {
                    operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    failure_category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_runtime_state", x => x.operation);
                    table.CheckConstraint("ck_monitoring_runtime_state_duration", "last_duration_ms IS NULL OR last_duration_ms >= 0");
                    table.CheckConstraint("ck_monitoring_runtime_state_failure_category", "failure_category IS NULL OR failure_category IN ('Database', 'Cancellation', 'QueueEnqueue', 'Unexpected')");
                    table.CheckConstraint("ck_monitoring_runtime_state_failures", "consecutive_failures >= 0");
                    table.CheckConstraint("ck_monitoring_runtime_state_operation", "operation IN ('monitoring-dispatch', 'monitoring-reconciliation')");
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_runtime_state",
                schema: "web_health");
        }
    }
}

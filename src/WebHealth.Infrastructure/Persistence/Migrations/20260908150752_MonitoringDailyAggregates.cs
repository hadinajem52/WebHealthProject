using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class MonitoringDailyAggregates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_daily_aggregate",
                schema: "web_health",
                columns: table => new
                {
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    utc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_count = table.Column<long>(type: "bigint", nullable: false),
                    scheduled_count = table.Column<long>(type: "bigint", nullable: false),
                    eligible_count = table.Column<long>(type: "bigint", nullable: false),
                    healthy_count = table.Column<long>(type: "bigint", nullable: false),
                    warning_count = table.Column<long>(type: "bigint", nullable: false),
                    down_count = table.Column<long>(type: "bigint", nullable: false),
                    maintenance_count = table.Column<long>(type: "bigint", nullable: false),
                    cancelled_count = table.Column<long>(type: "bigint", nullable: false),
                    excluded_count = table.Column<long>(type: "bigint", nullable: false),
                    duration_count = table.Column<long>(type: "bigint", nullable: false),
                    duration_sum_ms = table.Column<long>(type: "bigint", nullable: false),
                    duration_minimum_ms = table.Column<int>(type: "integer", nullable: true),
                    duration_maximum_ms = table.Column<int>(type: "integer", nullable: true),
                    histogram_version = table.Column<int>(type: "integer", nullable: false),
                    duration_histogram = table.Column<long[]>(type: "bigint[]", nullable: false),
                    comparability_identity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_comparable = table.Column<bool>(type: "boolean", nullable: false),
                    lowest_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    highest_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_deletion_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_daily_aggregate", x => new { x.endpoint_monitor_id, x.utc_date });
                    table.CheckConstraint("ck_monitoring_daily_aggregate_counts", "total_count > 0 AND scheduled_count BETWEEN 0 AND total_count AND eligible_count BETWEEN 0 AND scheduled_count AND healthy_count >= 0 AND warning_count >= 0 AND down_count >= 0 AND eligible_count::numeric = healthy_count::numeric + warning_count::numeric + down_count::numeric AND excluded_count >= 0 AND total_count::numeric = eligible_count::numeric + excluded_count::numeric AND maintenance_count BETWEEN 0 AND excluded_count AND cancelled_count BETWEEN 0 AND excluded_count");
                    table.CheckConstraint("ck_monitoring_daily_aggregate_dates", "first_measured_at <= last_measured_at AND (first_measured_at AT TIME ZONE 'UTC')::date = utc_date AND (last_measured_at AT TIME ZONE 'UTC')::date = utc_date AND (raw_deletion_started_at IS NULL OR raw_deletion_started_at >= computed_at)");
                    table.CheckConstraint("ck_monitoring_daily_aggregate_duration", "duration_count BETWEEN 0 AND eligible_count AND duration_sum_ms >= 0 AND ((duration_count = 0 AND duration_sum_ms = 0 AND duration_minimum_ms IS NULL AND duration_maximum_ms IS NULL) OR (duration_count > 0 AND duration_minimum_ms IS NOT NULL AND duration_maximum_ms IS NOT NULL AND duration_minimum_ms >= 0 AND duration_maximum_ms >= duration_minimum_ms AND duration_sum_ms::numeric BETWEEN duration_count::numeric * duration_minimum_ms AND duration_count::numeric * duration_maximum_ms))");
                    table.CheckConstraint("ck_monitoring_daily_aggregate_histogram", "histogram_version = 1 AND array_ndims(duration_histogram) = 1 AND array_lower(duration_histogram, 1) = 1 AND cardinality(duration_histogram) = 12 AND array_position(duration_histogram, NULL) IS NULL AND 0 <= ALL(duration_histogram) AND duration_count::numeric = duration_histogram[1]::numeric + duration_histogram[2]::numeric + duration_histogram[3]::numeric + duration_histogram[4]::numeric + duration_histogram[5]::numeric + duration_histogram[6]::numeric + duration_histogram[7]::numeric + duration_histogram[8]::numeric + duration_histogram[9]::numeric + duration_histogram[10]::numeric + duration_histogram[11]::numeric + duration_histogram[12]::numeric");
                    table.CheckConstraint("ck_monitoring_daily_aggregate_identity", "comparability_identity ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_monitoring_daily_aggregate_sources", "lowest_source IN ('Scheduled', 'Manual', 'Urgent') AND highest_source IN ('Scheduled', 'Manual', 'Urgent') AND lowest_source <= highest_source");
                    table.ForeignKey(
                        name: "fk_monitoring_daily_aggregate_endpoint_monitor_endpoint_monito~",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_daily_aggregate",
                schema: "web_health");
        }
    }
}

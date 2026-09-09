using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExactDailyReportSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_daily_aggregate_sources",
                schema: "web_health",
                table: "monitoring_daily_aggregate");

            migrationBuilder.AlterColumn<string>(
                name: "lowest_source",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "highest_source",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<int[]>(
                name: "exact_duration_samples",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                type: "integer[]",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_daily_aggregate_exact_samples",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                sql: "exact_duration_samples IS NULL OR (raw_deletion_started_at IS NULL AND array_ndims(exact_duration_samples) = 1 AND array_lower(exact_duration_samples, 1) = 1 AND array_position(exact_duration_samples, NULL) IS NULL AND 0 <= ALL(exact_duration_samples) AND cardinality(exact_duration_samples) = duration_count)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_daily_aggregate_sources",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                sql: "lowest_source <> '' AND highest_source <> '' AND lowest_source <= highest_source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_daily_aggregate_exact_samples",
                schema: "web_health",
                table: "monitoring_daily_aggregate");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_daily_aggregate_sources",
                schema: "web_health",
                table: "monitoring_daily_aggregate");

            migrationBuilder.DropColumn(
                name: "exact_duration_samples",
                schema: "web_health",
                table: "monitoring_daily_aggregate");

            migrationBuilder.Sql("""
                UPDATE web_health.monitoring_daily_aggregate
                SET lowest_source = 'Scheduled', highest_source = 'Scheduled'
                WHERE lowest_source NOT IN ('Scheduled', 'Manual', 'Urgent')
                    OR highest_source NOT IN ('Scheduled', 'Manual', 'Urgent')
                """);

            migrationBuilder.AlterColumn<string>(
                name: "lowest_source",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "highest_source",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_daily_aggregate_sources",
                schema: "web_health",
                table: "monitoring_daily_aggregate",
                sql: "lowest_source IN ('Scheduled', 'Manual', 'Urgent') AND highest_source IN ('Scheduled', 'Manual', 'Urgent') AND lowest_source <= highest_source");
        }
    }
}

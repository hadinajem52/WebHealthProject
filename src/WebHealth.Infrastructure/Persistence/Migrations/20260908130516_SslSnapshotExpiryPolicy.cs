using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class SslSnapshotExpiryPolicy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ssl_critical_expiry_days",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ssl_high_expiry_days",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ssl_warning_expiry_days",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE web_health.check_configuration_snapshot DISABLE TRIGGER trg_check_configuration_snapshot_immutable;
                UPDATE web_health.check_configuration_snapshot
                SET ssl_warning_expiry_days = 30, ssl_high_expiry_days = 15, ssl_critical_expiry_days = 7
                WHERE schema_version = 2 AND monitor_type = 'SslCertificate';
                ALTER TABLE web_health.check_configuration_snapshot ENABLE TRIGGER trg_check_configuration_snapshot_immutable;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_ssl_thresholds",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "(schema_version = 1 AND ssl_warning_expiry_days IS NULL AND ssl_high_expiry_days IS NULL AND ssl_critical_expiry_days IS NULL) OR (monitor_type <> 'SslCertificate' AND ssl_warning_expiry_days IS NULL AND ssl_high_expiry_days IS NULL AND ssl_critical_expiry_days IS NULL) OR (monitor_type = 'SslCertificate' AND ssl_warning_expiry_days IS NOT NULL AND ssl_high_expiry_days IS NOT NULL AND ssl_critical_expiry_days IS NOT NULL AND ssl_warning_expiry_days > ssl_high_expiry_days AND ssl_high_expiry_days > ssl_critical_expiry_days AND ssl_critical_expiry_days >= 0)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_ssl_thresholds",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "ssl_critical_expiry_days",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "ssl_high_expiry_days",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "ssl_warning_expiry_days",
                schema: "web_health",
                table: "check_configuration_snapshot");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RunHistoryArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "web_health",
                table: "png_audit_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "web_health",
                table: "page_audit_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "web_health",
                table: "logical_check",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "web_health",
                table: "crawl_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_png_audit_run_endpoint_archived",
                schema: "web_health",
                table: "png_audit_run",
                columns: new[] { "endpoint_id", "archived_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_png_audit_run_archived_status",
                schema: "web_health",
                table: "png_audit_run",
                sql: "archived_at IS NULL OR status IN ('Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_page_audit_run_endpoint_archived",
                schema: "web_health",
                table: "page_audit_run",
                columns: new[] { "endpoint_id", "archived_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_audit_run_archived_status",
                schema: "web_health",
                table: "page_audit_run",
                sql: "archived_at IS NULL OR status IN ('Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_logical_check_endpoint_monitor_archived",
                schema: "web_health",
                table: "logical_check",
                columns: new[] { "endpoint_monitor_id", "archived_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_logical_check_archived_state",
                schema: "web_health",
                table: "logical_check",
                sql: "archived_at IS NULL OR state = 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_crawl_run_endpoint_archived",
                schema: "web_health",
                table: "crawl_run",
                columns: new[] { "endpoint_id", "archived_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_crawl_run_archived_status",
                schema: "web_health",
                table: "crawl_run",
                sql: "archived_at IS NULL OR status <> 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_png_audit_run_endpoint_archived",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_png_audit_run_archived_status",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropIndex(
                name: "ix_page_audit_run_endpoint_archived",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_audit_run_archived_status",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.DropIndex(
                name: "ix_logical_check_endpoint_monitor_archived",
                schema: "web_health",
                table: "logical_check");

            migrationBuilder.DropCheckConstraint(
                name: "ck_logical_check_archived_state",
                schema: "web_health",
                table: "logical_check");

            migrationBuilder.DropIndex(
                name: "ix_crawl_run_endpoint_archived",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_crawl_run_archived_status",
                schema: "web_health",
                table: "crawl_run");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "web_health",
                table: "png_audit_run");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "web_health",
                table: "page_audit_run");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "web_health",
                table: "logical_check");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "web_health",
                table: "crawl_run");
        }
    }
}

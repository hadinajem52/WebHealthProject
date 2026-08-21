using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageAuditItemRestrictDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_page_audit_item_page_audit_run_run_id",
                schema: "web_health",
                table: "page_audit_item");

            migrationBuilder.AddForeignKey(
                name: "fk_page_audit_item_page_audit_run_run_id",
                schema: "web_health",
                table: "page_audit_item",
                column: "run_id",
                principalSchema: "web_health",
                principalTable: "page_audit_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_page_audit_item_page_audit_run_run_id",
                schema: "web_health",
                table: "page_audit_item");

            migrationBuilder.AddForeignKey(
                name: "fk_page_audit_item_page_audit_run_run_id",
                schema: "web_health",
                table: "page_audit_item",
                column: "run_id",
                principalSchema: "web_health",
                principalTable: "page_audit_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

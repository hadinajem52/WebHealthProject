using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class RetentionHolds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "retention_hold",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_hold", x => x.id);
                    table.CheckConstraint("ck_retention_hold_creator", "created_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_retention_hold_expiry", "expires_at IS NULL OR expires_at > created_at");
                    table.CheckConstraint("ck_retention_hold_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_retention_hold_release", "(released_at IS NULL AND released_by_user_id IS NULL) OR (released_at IS NOT NULL AND released_at >= created_at AND released_by_user_id IS NOT NULL AND released_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("ck_retention_hold_scope", "scope_type IN ('Client', 'Website', 'Environment', 'Endpoint', 'Monitor', 'LogicalCheck', 'Incident', 'CrawlRun', 'PageAuditRun') AND scope_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retention_hold",
                schema: "web_health");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropTargetAuthorizationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "target_authorization_evidence",
                schema: "web_health");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "target_authorization_evidence",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorization_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    normalized_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_target_authorization_evidence", x => x.id);
                    table.CheckConstraint("ck_target_authorization_evidence_expiry", "expires_at IS NULL OR expires_at > effective_from");
                    table.CheckConstraint("ck_target_authorization_evidence_kind", "authorization_kind IN ('Owned', 'ExplicitPermission')");
                    table.CheckConstraint("ck_target_authorization_evidence_port", "port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_target_authorization_evidence_revocation", "(revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR (revoked_at IS NOT NULL AND revoked_by_user_id IS NOT NULL AND revocation_reason IS NOT NULL AND length(revocation_reason) > 0)");
                    table.ForeignKey(
                        name: "fk_target_authorization_evidence_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_target_authorization_evidence_app_user_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_target_authorization_evidence_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_evidence_created_by_user_id",
                schema: "web_health",
                table: "target_authorization_evidence",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_evidence_endpoint_id_normalized_host_p~",
                schema: "web_health",
                table: "target_authorization_evidence",
                columns: new[] { "endpoint_id", "normalized_host", "port" });

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_evidence_revoked_by_user_id",
                schema: "web_health",
                table: "target_authorization_evidence",
                column: "revoked_by_user_id");
        }
    }
}

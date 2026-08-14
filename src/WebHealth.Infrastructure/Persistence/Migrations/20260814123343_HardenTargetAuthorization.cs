using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenTargetAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "effective_port",
                schema: "web_health",
                table: "endpoint",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "normalized_host",
                schema: "web_health",
                table: "endpoint",
                type: "character varying(253)",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "target_authorization",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorization_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_target_authorization", x => x.id);
                    table.CheckConstraint("ck_target_authorization_expiry", "expires_at IS NULL OR expires_at > effective_from");
                    table.CheckConstraint("ck_target_authorization_kind", "authorization_kind IN ('Owned', 'ExplicitPermission')");
                    table.CheckConstraint("ck_target_authorization_port", "port BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "fk_target_authorization_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_target_authorization_app_user_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_target_authorization_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                UPDATE web_health.endpoint
                SET normalized_host = trim(both '[]' from (regexp_match(
                        normalized_url,
                        '^https?://(\\[[^]]+\\]|[^/:]+)'))[1]),
                    effective_port = COALESCE(
                        ((regexp_match(
                            normalized_url,
                            '^https?://(?:\\[[^]]+\\]|[^/:]+):([0-9]+)'))[1])::integer,
                        CASE WHEN normalized_url LIKE 'https://%' THEN 443 ELSE 80 END);

                ALTER TABLE web_health.endpoint ALTER COLUMN normalized_host DROP DEFAULT;
                ALTER TABLE web_health.endpoint ALTER COLUMN effective_port DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_effective_port",
                schema: "web_health",
                table: "endpoint",
                sql: "effective_port BETWEEN 1 AND 65535");

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_normalized_host",
                schema: "web_health",
                table: "endpoint",
                sql: "length(normalized_host) > 0");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION web_health.enforce_production_endpoint()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    PERFORM web_health.validate_production_endpoint(
                        NEW.id,
                        CASE WHEN TG_OP = 'INSERT' THEN TRUE ELSE
                            NEW.environment_id IS DISTINCT FROM OLD.environment_id
                            OR (NEW.normalized_url LIKE 'http://%'
                                AND NEW.normalized_url IS DISTINCT FROM OLD.normalized_url)
                            OR NEW.http_exception_reason IS DISTINCT FROM OLD.http_exception_reason
                            OR NEW.http_exception_approved_by_user_id IS DISTINCT FROM OLD.http_exception_approved_by_user_id
                            OR NEW.http_exception_approved_at IS DISTINCT FROM OLD.http_exception_approved_at
                        END);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.enforce_production_environment_endpoints()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_endpoint_id uuid;
                    verify_approver_role boolean;
                BEGIN
                    verify_approver_role = TG_OP = 'INSERT' OR NOT OLD.is_production;
                    IF NEW.is_production THEN
                        FOR target_endpoint_id IN
                            SELECT endpoint.id
                            FROM web_health.endpoint AS endpoint
                            WHERE endpoint.environment_id = NEW.id
                              AND endpoint.deleted_at IS NULL
                        LOOP
                            PERFORM web_health.validate_production_endpoint(
                                target_endpoint_id,
                                verify_approver_role);
                        END LOOP;
                    END IF;
                    RETURN NULL;
                END;
                $function$;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_created_by_user_id",
                schema: "web_health",
                table: "target_authorization",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_endpoint_id_effective_from_expires_at",
                schema: "web_health",
                table: "target_authorization",
                columns: new[] { "endpoint_id", "effective_from", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_endpoint_id_normalized_host_port",
                schema: "web_health",
                table: "target_authorization",
                columns: new[] { "endpoint_id", "normalized_host", "port" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_revoked_by_user_id",
                schema: "web_health",
                table: "target_authorization",
                column: "revoked_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION web_health.enforce_production_endpoint()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    PERFORM web_health.validate_production_endpoint(
                        NEW.id,
                        CASE WHEN TG_OP = 'INSERT' THEN TRUE ELSE
                            NEW.http_exception_reason IS DISTINCT FROM OLD.http_exception_reason
                            OR NEW.http_exception_approved_by_user_id IS DISTINCT FROM OLD.http_exception_approved_by_user_id
                            OR NEW.http_exception_approved_at IS DISTINCT FROM OLD.http_exception_approved_at
                        END);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.enforce_production_environment_endpoints()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_endpoint_id uuid;
                BEGIN
                    IF NEW.is_production THEN
                        FOR target_endpoint_id IN
                            SELECT endpoint.id
                            FROM web_health.endpoint AS endpoint
                            WHERE endpoint.environment_id = NEW.id
                              AND endpoint.deleted_at IS NULL
                        LOOP
                            PERFORM web_health.validate_production_endpoint(target_endpoint_id, FALSE);
                        END LOOP;
                    END IF;
                    RETURN NULL;
                END;
                $function$;
                """);

            migrationBuilder.DropTable(
                name: "target_authorization",
                schema: "web_health");

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_effective_port",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_normalized_host",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropColumn(
                name: "effective_port",
                schema: "web_health",
                table: "endpoint");

            migrationBuilder.DropColumn(
                name: "normalized_host",
                schema: "web_health",
                table: "endpoint");
        }
    }
}

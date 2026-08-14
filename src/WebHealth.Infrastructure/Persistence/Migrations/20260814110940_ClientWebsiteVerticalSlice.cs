using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientWebsiteVerticalSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "website",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    technology_cms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website", x => x.id);
                    table.ForeignKey(
                        name: "fk_website_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "web_health",
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "environment",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    website_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    environment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_production = table.Column<bool>(type: "boolean", nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_environment", x => x.id);
                    table.CheckConstraint("ck_environment_type_matches_production", "(environment_type = 'Production') = is_production");
                    table.ForeignKey(
                        name: "fk_environment_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_environment_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_environment_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_environment_website_website_id",
                        column: x => x.website_id,
                        principalSchema: "web_health",
                        principalTable: "website",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_grant",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    website_id = table.Column<Guid>(type: "uuid", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_grant", x => x.id);
                    table.CheckConstraint("ck_access_grant_access_level", "access_level IN ('Read', 'Manage')");
                    table.CheckConstraint("ck_access_grant_exactly_one_scope", "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int = 1");
                    table.CheckConstraint("ck_access_grant_expiry", "expires_at IS NULL OR expires_at > effective_from");
                    table.ForeignKey(
                        name: "fk_access_grant_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_app_user_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "web_health",
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_environment_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "web_health",
                        principalTable: "environment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_website_website_id",
                        column: x => x.website_id,
                        principalSchema: "web_health",
                        principalTable: "website",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_client_id",
                schema: "web_health",
                table: "access_grant",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_created_by_user_id",
                schema: "web_health",
                table: "access_grant",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_environment_id",
                schema: "web_health",
                table: "access_grant",
                column: "environment_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_revoked_by_user_id",
                schema: "web_health",
                table: "access_grant",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_client_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "client_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_environment_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "environment_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_website_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "website_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_website_id",
                schema: "web_health",
                table: "access_grant",
                column: "website_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_created_by_user_id",
                schema: "web_health",
                table: "client",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_deleted_at_is_active_name",
                schema: "web_health",
                table: "client",
                columns: new[] { "deleted_at", "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_client_deleted_by_user_id",
                schema: "web_health",
                table: "client",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_normalized_name_normalization_version",
                schema: "web_health",
                table: "client",
                columns: new[] { "normalized_name", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_client_owner_subject_id",
                schema: "web_health",
                table: "client",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_updated_by_user_id",
                schema: "web_health",
                table: "client",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_created_by_user_id",
                schema: "web_health",
                table: "environment",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_deleted_by_user_id",
                schema: "web_health",
                table: "environment",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_updated_by_user_id",
                schema: "web_health",
                table: "environment",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_website_id_deleted_at_is_active",
                schema: "web_health",
                table: "environment",
                columns: new[] { "website_id", "deleted_at", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_environment_website_id_normalized_name_normalization_version",
                schema: "web_health",
                table: "environment",
                columns: new[] { "website_id", "normalized_name", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_website_client_id_deleted_at_is_enabled",
                schema: "web_health",
                table: "website",
                columns: new[] { "client_id", "deleted_at", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_website_client_id_normalized_name_normalization_version",
                schema: "web_health",
                table: "website",
                columns: new[] { "client_id", "normalized_name", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_website_created_by_user_id",
                schema: "web_health",
                table: "website",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_deleted_by_user_id",
                schema: "web_health",
                table: "website",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_owner_subject_id",
                schema: "web_health",
                table: "website",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_updated_by_user_id",
                schema: "web_health",
                table: "website",
                column: "updated_by_user_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION web_health.enforce_enabled_website_environment()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW.is_enabled
                       AND NOT EXISTS (
                           SELECT 1
                           FROM web_health.environment AS environment
                           WHERE environment.website_id = NEW.id
                             AND environment.is_active
                             AND environment.deleted_at IS NULL)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_website_enabled_requires_active_environment',
                            MESSAGE = 'An enabled website requires at least one active environment.';
                    END IF;

                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_website_enabled_requires_active_environment
                AFTER INSERT OR UPDATE ON web_health.website
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_enabled_website_environment();

                CREATE FUNCTION web_health.enforce_environment_keeps_enabled_website_valid()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    affected_website_id uuid;
                BEGIN
                    FOR affected_website_id IN
                        SELECT DISTINCT candidate.website_id
                        FROM (VALUES
                            (CASE WHEN TG_OP <> 'INSERT' THEN OLD.website_id END),
                            (CASE WHEN TG_OP <> 'DELETE' THEN NEW.website_id END))
                            AS candidate(website_id)
                        WHERE candidate.website_id IS NOT NULL
                    LOOP
                        IF EXISTS (
                               SELECT 1
                               FROM web_health.website AS website
                               WHERE website.id = affected_website_id
                                 AND website.is_enabled)
                           AND NOT EXISTS (
                               SELECT 1
                               FROM web_health.environment AS environment
                               WHERE environment.website_id = affected_website_id
                                 AND environment.is_active
                                 AND environment.deleted_at IS NULL)
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                CONSTRAINT = 'ck_website_enabled_requires_active_environment',
                                MESSAGE = 'An enabled website requires at least one active environment.';
                        END IF;
                    END LOOP;

                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_environment_keeps_enabled_website_valid
                AFTER INSERT OR UPDATE OR DELETE ON web_health.environment
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_environment_keeps_enabled_website_valid();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS ck_environment_keeps_enabled_website_valid
                    ON web_health.environment;
                DROP TRIGGER IF EXISTS ck_website_enabled_requires_active_environment
                    ON web_health.website;
                DROP FUNCTION IF EXISTS web_health.enforce_environment_keeps_enabled_website_valid();
                DROP FUNCTION IF EXISTS web_health.enforce_enabled_website_environment();
                """);

            migrationBuilder.DropTable(
                name: "access_grant",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "environment",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "website",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "client",
                schema: "web_health");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnvironmentEndpointVerticalSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_access_grant_exactly_one_scope",
                schema: "web_health",
                table: "access_grant");

            migrationBuilder.AddColumn<Guid>(
                name: "endpoint_id",
                schema: "web_health",
                table: "access_grant",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "endpoint",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_url_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    http_exception_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    http_exception_approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    http_exception_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_endpoint", x => x.id);
                    table.CheckConstraint("ck_endpoint_http_exception_complete", "(http_exception_reason IS NULL AND http_exception_approved_by_user_id IS NULL AND http_exception_approved_at IS NULL) OR (http_exception_reason IS NOT NULL AND http_exception_approved_by_user_id IS NOT NULL AND http_exception_approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_endpoint_normalized_scheme", "normalized_url LIKE 'http://%' OR normalized_url LIKE 'https://%'");
                    table.CheckConstraint("ck_endpoint_url_hash_length", "octet_length(normalized_url_hash) = 32");
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_http_exception_approved_by_user_id",
                        column: x => x.http_exception_approved_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_environment_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "web_health",
                        principalTable: "environment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "policy_profile",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    monitor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bounded_settings = table.Column<string>(type: "jsonb", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "endpoint_monitor",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bounded_overrides = table.Column<string>(type: "jsonb", nullable: false),
                    schedule_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    configuration_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    failure_confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    recovery_confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    warning_threshold_ms = table.Column<int>(type: "integer", nullable: true),
                    critical_threshold_ms = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_endpoint_monitor", x => x.id);
                    table.CheckConstraint("ck_endpoint_monitor_positive_confirmation", "failure_confirmation_count > 0 AND recovery_confirmation_count > 0");
                    table.CheckConstraint("ck_endpoint_monitor_positive_interval", "interval_seconds > 0");
                    table.CheckConstraint("ck_endpoint_monitor_positive_timeout", "timeout_seconds > 0");
                    table.CheckConstraint("ck_endpoint_monitor_threshold_order", "warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms < critical_threshold_ms");
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_policy_profile_policy_profile_id",
                        column: x => x.policy_profile_id,
                        principalSchema: "web_health",
                        principalTable: "policy_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "web_health",
                table: "policy_profile",
                columns: new[] { "id", "bounded_settings", "created_at", "deleted_at", "is_system", "monitor_type", "name", "version" },
                values: new object[] { new Guid("fd3c8021-ff54-4f31-a3ad-2010b7b193dd"), "{}", new DateTimeOffset(new DateTime(2026, 8, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "HttpAvailability", "Default HTTP availability", 1L });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_endpoint_id",
                schema: "web_health",
                table: "access_grant",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_endpoint_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "endpoint_id", "effective_from" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_grant_exactly_one_scope",
                schema: "web_health",
                table: "access_grant",
                sql: "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int + (endpoint_id IS NOT NULL)::int = 1");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_created_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_deleted_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_environment_id_deleted_at_is_enabled",
                schema: "web_health",
                table: "endpoint",
                columns: new[] { "environment_id", "deleted_at", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_http_exception_approved_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "http_exception_approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_owner_subject_id",
                schema: "web_health",
                table: "endpoint",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_updated_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_endpoint_environment_url_hash_version_active",
                schema: "web_health",
                table: "endpoint",
                columns: new[] { "environment_id", "normalized_url_hash", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_created_by_user_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_deleted_by_user_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_endpoint_id_monitor_type",
                schema: "web_health",
                table: "endpoint_monitor",
                columns: new[] { "endpoint_id", "monitor_type" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_policy_profile_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "policy_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_updated_by_user_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_policy_profile_name_monitor_type",
                schema: "web_health",
                table: "policy_profile",
                columns: new[] { "name", "monitor_type" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_access_grant_endpoint_endpoint_id",
                schema: "web_health",
                table: "access_grant",
                column: "endpoint_id",
                principalSchema: "web_health",
                principalTable: "endpoint",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION web_health.validate_production_endpoint(
                    target_endpoint_id uuid,
                    verify_approver_role boolean)
                RETURNS void
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target record;
                BEGIN
                    SELECT endpoint.normalized_url,
                           endpoint.http_exception_reason,
                           endpoint.http_exception_approved_by_user_id,
                           endpoint.http_exception_approved_at,
                           environment.is_production
                    INTO target
                    FROM web_health.endpoint AS endpoint
                    JOIN web_health.environment AS environment
                      ON environment.id = endpoint.environment_id
                    WHERE endpoint.id = target_endpoint_id;

                    IF target.is_production
                       AND target.normalized_url LIKE 'http://%'
                       AND (target.http_exception_reason IS NULL
                            OR length(btrim(target.http_exception_reason)) = 0
                            OR target.http_exception_approved_by_user_id IS NULL
                            OR target.http_exception_approved_at IS NULL
                            OR (verify_approver_role AND NOT EXISTS (
                                SELECT 1
                                FROM web_health.app_user_role AS user_role
                                JOIN web_health.app_role AS role ON role.id = user_role.role_id
                                WHERE user_role.user_id = target.http_exception_approved_by_user_id
                                  AND role.normalized_name = 'ADMINISTRATOR')))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_production_http_endpoint_admin_exception',
                            MESSAGE = 'Production HTTP endpoints require an Administrator-approved exception reason.';
                    END IF;
                END;
                $function$;

                CREATE FUNCTION web_health.enforce_production_endpoint()
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

                CREATE CONSTRAINT TRIGGER ck_production_http_endpoint_admin_exception
                AFTER INSERT OR UPDATE ON web_health.endpoint
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_production_endpoint();

                CREATE FUNCTION web_health.enforce_production_environment_endpoints()
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

                CREATE CONSTRAINT TRIGGER ck_production_environment_endpoints
                AFTER INSERT OR UPDATE ON web_health.environment
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_production_environment_endpoints();

                CREATE FUNCTION web_health.enforce_monitor_policy_type()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM web_health.policy_profile AS profile
                        WHERE profile.id = NEW.policy_profile_id
                          AND profile.monitor_type = NEW.monitor_type
                          AND profile.deleted_at IS NULL)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_endpoint_monitor_policy_type',
                            MESSAGE = 'Endpoint monitor type must match its active policy profile.';
                    END IF;
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_endpoint_monitor_policy_type
                AFTER INSERT OR UPDATE ON web_health.endpoint_monitor
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_monitor_policy_type();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS ck_endpoint_monitor_policy_type ON web_health.endpoint_monitor;
                DROP TRIGGER IF EXISTS ck_production_environment_endpoints ON web_health.environment;
                DROP TRIGGER IF EXISTS ck_production_http_endpoint_admin_exception ON web_health.endpoint;
                DROP FUNCTION IF EXISTS web_health.enforce_monitor_policy_type();
                DROP FUNCTION IF EXISTS web_health.enforce_production_environment_endpoints();
                DROP FUNCTION IF EXISTS web_health.enforce_production_endpoint();
                DROP FUNCTION IF EXISTS web_health.validate_production_endpoint(uuid, boolean);
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_access_grant_endpoint_endpoint_id",
                schema: "web_health",
                table: "access_grant");

            migrationBuilder.DropTable(
                name: "endpoint_monitor",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "endpoint",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "policy_profile",
                schema: "web_health");

            migrationBuilder.DropIndex(
                name: "ix_access_grant_endpoint_id",
                schema: "web_health",
                table: "access_grant");

            migrationBuilder.DropIndex(
                name: "ix_access_grant_user_id_endpoint_id_effective_from",
                schema: "web_health",
                table: "access_grant");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_grant_exactly_one_scope",
                schema: "web_health",
                table: "access_grant");

            migrationBuilder.DropColumn(
                name: "endpoint_id",
                schema: "web_health",
                table: "access_grant");

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_grant_exactly_one_scope",
                schema: "web_health",
                table: "access_grant",
                sql: "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int = 1");
        }
    }
}

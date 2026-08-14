using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAccessAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "web_health");

            migrationBuilder.CreateTable(
                name: "app_role",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_disabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_role_claim",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_role_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_role_claim_app_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "web_health",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_user_claim",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_user_claim_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_user_login",
                schema: "web_health",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_app_user_login_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_user_role",
                schema: "web_health",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_role", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_app_user_role_app_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "web_health",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_app_user_role_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_user_token",
                schema: "web_health",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_app_user_token_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_identifier = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_identifier = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    request_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    before_values = table.Column<string>(type: "jsonb", nullable: true),
                    after_values = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_event_app_user_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    is_disabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team", x => x.id);
                    table.ForeignKey(
                        name: "fk_team_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "owner_subject",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owner_subject", x => x.id);
                    table.CheckConstraint("ck_owner_subject_exactly_one_subject", "(user_id IS NOT NULL)::int + (team_id IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "fk_owner_subject_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_owner_subject_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_owner_subject_team_team_id",
                        column: x => x.team_id,
                        principalSchema: "web_health",
                        principalTable: "team",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_member",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_member", x => x.id);
                    table.CheckConstraint("ck_team_member_effective_range", "effective_until IS NULL OR effective_until > effective_from");
                    table.ForeignKey(
                        name: "fk_team_member_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_member_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_member_team_team_id",
                        column: x => x.team_id,
                        principalSchema: "web_health",
                        principalTable: "team",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "role_name_index",
                schema: "web_health",
                table: "app_role",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_role_claim_role_id",
                schema: "web_health",
                table: "app_role_claim",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "email_index",
                schema: "web_health",
                table: "app_user",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "user_name_index",
                schema: "web_health",
                table: "app_user",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_user_claim_user_id",
                schema: "web_health",
                table: "app_user_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_login_user_id",
                schema: "web_health",
                table: "app_user_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_role_role_id",
                schema: "web_health",
                table: "app_user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_action_occurred_at",
                schema: "web_health",
                table: "audit_event",
                columns: new[] { "action", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_actor_user_id_occurred_at",
                schema: "web_health",
                table: "audit_event",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_entity_type_entity_identifier_occurred_at",
                schema: "web_health",
                table: "audit_event",
                columns: new[] { "entity_type", "entity_identifier", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_occurred_at",
                schema: "web_health",
                table: "audit_event",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_owner_subject_created_by_user_id",
                schema: "web_health",
                table: "owner_subject",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_owner_subject_team_id",
                schema: "web_health",
                table: "owner_subject",
                column: "team_id",
                unique: true,
                filter: "team_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_owner_subject_user_id",
                schema: "web_health",
                table: "owner_subject",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_team_created_by_user_id",
                schema: "web_health",
                table: "team",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_normalized_name_normalization_version",
                schema: "web_health",
                table: "team",
                columns: new[] { "normalized_name", "normalization_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_updated_by_user_id",
                schema: "web_health",
                table: "team",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_member_created_by_user_id",
                schema: "web_health",
                table: "team_member",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_member_team_id_user_id_effective_from",
                schema: "web_health",
                table: "team_member",
                columns: new[] { "team_id", "user_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_team_member_user_id_effective_until",
                schema: "web_health",
                table: "team_member",
                columns: new[] { "user_id", "effective_until" });

            migrationBuilder.Sql("""
                INSERT INTO web_health.owner_subject
                    (id, user_id, team_id, created_at, created_by_user_id)
                SELECT gen_random_uuid(), id, NULL, created_at, id
                FROM web_health.app_user;

                CREATE EXTENSION IF NOT EXISTS btree_gist;

                ALTER TABLE web_health.team_member
                ADD CONSTRAINT ex_team_member_no_overlapping_periods
                EXCLUDE USING gist (
                    team_id WITH =,
                    user_id WITH =,
                    tstzrange(effective_from, effective_until, '[)') WITH &&
                );

                CREATE FUNCTION web_health.prevent_audit_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'audit_event rows are append-only';
                END;
                $function$;

                CREATE TRIGGER trg_audit_event_append_only
                BEFORE UPDATE OR DELETE ON web_health.audit_event
                FOR EACH ROW EXECUTE FUNCTION web_health.prevent_audit_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_event_append_only ON web_health.audit_event;
                DROP FUNCTION IF EXISTS web_health.prevent_audit_event_mutation();
                ALTER TABLE web_health.team_member
                    DROP CONSTRAINT IF EXISTS ex_team_member_no_overlapping_periods;
                """);

            migrationBuilder.DropTable(
                name: "app_role_claim",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "app_user_claim",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "app_user_login",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "app_user_role",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "app_user_token",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "owner_subject",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "team_member",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "app_role",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "team",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "web_health");
        }
    }
}

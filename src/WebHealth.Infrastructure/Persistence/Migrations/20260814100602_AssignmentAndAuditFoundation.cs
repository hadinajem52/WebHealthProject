using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentAndAuditFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "request_method",
                schema: "web_health",
                table: "audit_event",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "correlation_id",
                schema: "web_health",
                table: "audit_event",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<string>(
                name: "after_values",
                schema: "web_health",
                table: "audit_event",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "before_values",
                schema: "web_health",
                table: "audit_event",
                type: "jsonb",
                nullable: true);

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
                name: "ix_audit_event_entity_type_entity_identifier_occurred_at",
                schema: "web_health",
                table: "audit_event",
                columns: new[] { "entity_type", "entity_identifier", "occurred_at" });

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

            migrationBuilder.Sql(
                """
                INSERT INTO web_health.owner_subject
                    (id, user_id, team_id, created_at, created_by_user_id)
                SELECT gen_random_uuid(), id, NULL, created_at, id
                FROM web_health.app_user;
                """);

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                """
                ALTER TABLE web_health.team_member
                ADD CONSTRAINT ex_team_member_no_overlapping_periods
                EXCLUDE USING gist (
                    team_id WITH =,
                    user_id WITH =,
                    tstzrange(effective_from, effective_until, '[)') WITH &&
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE FUNCTION web_health.prevent_audit_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_event rows are append-only';
                END;
                $$;

                CREATE TRIGGER trg_audit_event_append_only
                BEFORE UPDATE OR DELETE ON web_health.audit_event
                FOR EACH ROW EXECUTE FUNCTION web_health.prevent_audit_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_event_append_only ON web_health.audit_event;
                DROP FUNCTION IF EXISTS web_health.prevent_audit_event_mutation();
                """);
            migrationBuilder.Sql(
                "ALTER TABLE web_health.team_member DROP CONSTRAINT IF EXISTS ex_team_member_no_overlapping_periods;");

            migrationBuilder.DropTable(
                name: "owner_subject",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "team_member",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "team",
                schema: "web_health");

            migrationBuilder.DropIndex(
                name: "ix_audit_event_entity_type_entity_identifier_occurred_at",
                schema: "web_health",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "after_values",
                schema: "web_health",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "before_values",
                schema: "web_health",
                table: "audit_event");

            migrationBuilder.AlterColumn<string>(
                name: "request_method",
                schema: "web_health",
                table: "audit_event",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "correlation_id",
                schema: "web_health",
                table: "audit_event",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HealthMaintenanceAndIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_maintenance",
                schema: "web_health",
                table: "check_result",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "maintenance_occurrence_id",
                schema: "web_health",
                table: "check_result",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "endpoint_health",
                schema: "web_health",
                columns: table => new
                {
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_logical_check_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoint_health", x => x.endpoint_monitor_id);
                    table.CheckConstraint("ck_endpoint_health_status", "confirmed_status IN ('Unknown', 'Healthy', 'Warning', 'Critical', 'Disabled')");
                    table.ForeignKey(
                        name: "fk_endpoint_health_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_health_logical_check_evidence_logical_check_id",
                        column: x => x.evidence_logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issue_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recurrence_count = table.Column<int>(type: "integer", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident", x => x.id);
                    table.CheckConstraint("ck_incident_acknowledged_required", "status NOT IN ('Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') OR acknowledged_at IS NOT NULL");
                    table.CheckConstraint("ck_incident_closed_required", "status <> 'Closed' OR closed_at IS NOT NULL");
                    table.CheckConstraint("ck_incident_lifecycle_order", "(acknowledged_at IS NULL OR acknowledged_at >= opened_at) AND (resolved_at IS NULL OR resolved_at >= opened_at) AND (closed_at IS NULL OR (resolved_at IS NOT NULL AND closed_at >= resolved_at))");
                    table.CheckConstraint("ck_incident_recurrence_count", "recurrence_count >= 0");
                    table.CheckConstraint("ck_incident_resolution_complete", "(status NOT IN ('Resolved', 'Closed') AND resolution_category IS NULL AND resolution_note IS NULL AND resolved_at IS NULL) OR (status IN ('Resolved', 'Closed') AND resolution_category IS NOT NULL AND resolution_note IS NOT NULL AND resolved_at IS NOT NULL)");
                    table.CheckConstraint("ck_incident_severity", "severity IN ('Warning', 'Critical')");
                    table.CheckConstraint("ck_incident_status", "status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed')");
                    table.ForeignKey(
                        name: "fk_incident_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_incident_previous_incident_id",
                        column: x => x.previous_incident_id,
                        principalSchema: "web_health",
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "issue_state",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    consecutive_recoveries = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_state", x => x.id);
                    table.CheckConstraint("ck_issue_state_counters", "consecutive_failures >= 0 AND consecutive_recoveries >= 0");
                    table.ForeignKey(
                        name: "fk_issue_state_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_window",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    timezone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    suppression_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pause_escalation = table.Column<bool>(type: "boolean", nullable: false),
                    continue_failure_counter = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_window", x => x.id);
                    table.CheckConstraint("ck_maintenance_window_reason", "length(reason) > 0");
                    table.CheckConstraint("ck_maintenance_window_suppression_policy", "suppression_policy IN ('SuppressAll', 'None')");
                    table.CheckConstraint("ck_maintenance_window_updated", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_maintenance_window_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_window_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_window_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident_event",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    from_owner_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_owner_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bounded_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_event", x => x.id);
                    table.CheckConstraint("ck_incident_event_sequence_number", "sequence_number > 0");
                    table.CheckConstraint("ck_incident_event_type", "event_type IN ('Opened', 'StatusChanged', 'Reassigned', 'NoteAdded')");
                    table.ForeignKey(
                        name: "fk_incident_event_app_user_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_event_incident_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "web_health",
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_event_owner_subject_from_owner_subject_id",
                        column: x => x.from_owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_event_owner_subject_to_owner_subject_id",
                        column: x => x.to_owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident_evidence",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bounded_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_evidence", x => x.id);
                    table.CheckConstraint("ck_incident_evidence_type", "evidence_type IN ('Opening', 'Failure', 'Recovery')");
                    table.ForeignKey(
                        name: "fk_incident_evidence_incident_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "web_health",
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_occurrence",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_window_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_occurrence", x => x.id);
                    table.CheckConstraint("ck_maintenance_occurrence_interval", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_maintenance_occurrence_maintenance_window_id",
                        column: x => x.maintenance_window_id,
                        principalSchema: "web_health",
                        principalTable: "maintenance_window",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_target",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_window_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    website_id = table.Column<Guid>(type: "uuid", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_target", x => x.id);
                    table.CheckConstraint("ck_maintenance_target_exactly_one_scope", "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int + (endpoint_id IS NOT NULL)::int + (endpoint_monitor_id IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "fk_maintenance_target_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "web_health",
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_target_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_target_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_target_environment_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "web_health",
                        principalTable: "environment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_target_maintenance_window_maintenance_window_id",
                        column: x => x.maintenance_window_id,
                        principalSchema: "web_health",
                        principalTable: "maintenance_window",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_target_website_website_id",
                        column: x => x.website_id,
                        principalSchema: "web_health",
                        principalTable: "website",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_check_result_maintenance_occurrence_id",
                schema: "web_health",
                table: "check_result",
                column: "maintenance_occurrence_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_maintenance",
                schema: "web_health",
                table: "check_result",
                sql: "(is_maintenance AND maintenance_occurrence_id IS NOT NULL) OR (NOT is_maintenance AND maintenance_occurrence_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_health_evidence_logical_check_id",
                schema: "web_health",
                table: "endpoint_health",
                column: "evidence_logical_check_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_endpoint_monitor_id_issue_key",
                schema: "web_health",
                table: "incident",
                columns: new[] { "endpoint_monitor_id", "issue_key" },
                unique: true,
                filter: "status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery')");

            migrationBuilder.CreateIndex(
                name: "ix_incident_owner_subject_id",
                schema: "web_health",
                table: "incident",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_previous_incident_id",
                schema: "web_health",
                table: "incident",
                column: "previous_incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_severity_opened_at",
                schema: "web_health",
                table: "incident",
                columns: new[] { "status", "severity", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_event_actor_user_id",
                schema: "web_health",
                table: "incident_event",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_event_from_owner_subject_id",
                schema: "web_health",
                table: "incident_event",
                column: "from_owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_event_incident_id_sequence_number",
                schema: "web_health",
                table: "incident_event",
                columns: new[] { "incident_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incident_event_to_owner_subject_id",
                schema: "web_health",
                table: "incident_event",
                column: "to_owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_incident_id",
                schema: "web_health",
                table: "incident_evidence",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_logical_check_id",
                schema: "web_health",
                table: "incident_evidence",
                column: "logical_check_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_state_endpoint_monitor_id_issue_key",
                schema: "web_health",
                table: "issue_state",
                columns: new[] { "endpoint_monitor_id", "issue_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_occurrence_window_interval",
                schema: "web_health",
                table: "maintenance_occurrence",
                columns: new[] { "maintenance_window_id", "starts_at", "ends_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_target_client_id",
                schema: "web_health",
                table: "maintenance_target",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_target_endpoint_id",
                schema: "web_health",
                table: "maintenance_target",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_target_endpoint_monitor_id",
                schema: "web_health",
                table: "maintenance_target",
                column: "endpoint_monitor_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_target_environment_id",
                schema: "web_health",
                table: "maintenance_target",
                column: "environment_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_target_maintenance_window_id",
                schema: "web_health",
                table: "maintenance_target",
                column: "maintenance_window_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_target_website_id",
                schema: "web_health",
                table: "maintenance_target",
                column: "website_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_window_created_by_user_id",
                schema: "web_health",
                table: "maintenance_window",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_window_deleted_at",
                schema: "web_health",
                table: "maintenance_window",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_window_deleted_by_user_id",
                schema: "web_health",
                table: "maintenance_window",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_window_updated_by_user_id",
                schema: "web_health",
                table: "maintenance_window",
                column: "updated_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_check_result_maintenance_occurrence_id",
                schema: "web_health",
                table: "check_result",
                column: "maintenance_occurrence_id",
                principalSchema: "web_health",
                principalTable: "maintenance_occurrence",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION web_health.reject_incident_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'incident_event rows are immutable';
                END;
                $function$;

                CREATE TRIGGER trg_incident_event_immutable
                BEFORE UPDATE OR DELETE ON web_health.incident_event
                FOR EACH ROW
                EXECUTE FUNCTION web_health.reject_incident_event_mutation();

                CREATE FUNCTION web_health.reject_incident_evidence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'incident_evidence rows are immutable';
                END;
                $function$;

                CREATE TRIGGER trg_incident_evidence_immutable
                BEFORE UPDATE OR DELETE ON web_health.incident_evidence
                FOR EACH ROW
                EXECUTE FUNCTION web_health.reject_incident_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_incident_evidence_immutable
                    ON web_health.incident_evidence;
                DROP FUNCTION IF EXISTS web_health.reject_incident_evidence_mutation();
                DROP TRIGGER IF EXISTS trg_incident_event_immutable
                    ON web_health.incident_event;
                DROP FUNCTION IF EXISTS web_health.reject_incident_event_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_check_result_maintenance_occurrence_id",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropTable(
                name: "endpoint_health",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "incident_event",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "incident_evidence",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "issue_state",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "maintenance_occurrence",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "maintenance_target",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "incident",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "maintenance_window",
                schema: "web_health");

            migrationBuilder.DropIndex(
                name: "ix_check_result_maintenance_occurrence_id",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_maintenance",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropColumn(
                name: "is_maintenance",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropColumn(
                name: "maintenance_occurrence_id",
                schema: "web_health",
                table: "check_result");
        }
    }
}

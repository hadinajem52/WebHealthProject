using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_evidence_type",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_acknowledged_fields",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_lifecycle_order",
                schema: "web_health",
                table: "incident");

            migrationBuilder.AlterColumn<Guid>(
                name: "logical_check_id",
                schema: "web_health",
                table: "incident_evidence",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_user_id",
                schema: "web_health",
                table: "incident_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "outage_duration_ms",
                schema: "web_health",
                table: "incident",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "recovery_duration_ms",
                schema: "web_health",
                table: "incident",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recovery_started_at",
                schema: "web_health",
                table: "incident",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_actor_user_id",
                schema: "web_health",
                table: "incident_evidence",
                column: "actor_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_evidence_source",
                schema: "web_health",
                table: "incident_evidence",
                sql: "(evidence_type IN ('Opening', 'Failure', 'Recovery') AND logical_check_id IS NOT NULL AND actor_user_id IS NULL) OR (evidence_type = 'Resolution' AND ((logical_check_id IS NOT NULL AND actor_user_id IS NULL) OR (logical_check_id IS NULL AND actor_user_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_evidence_type",
                schema: "web_health",
                table: "incident_evidence",
                sql: "evidence_type IN ('Opening', 'Failure', 'Recovery', 'Resolution')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event",
                sql: "(event_type = 'Opened' AND to_status IS NOT NULL AND from_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL) OR (event_type = 'StatusChanged' AND from_status IS NOT NULL AND to_status IS NOT NULL AND from_status <> to_status AND from_status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') AND to_status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL) OR (event_type = 'Reassigned' AND from_status IS NULL AND to_status IS NULL AND to_owner_subject_id IS NOT NULL AND (from_owner_subject_id IS NULL OR from_owner_subject_id <> to_owner_subject_id)) OR (event_type = 'NoteAdded' AND from_status IS NULL AND to_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL AND bounded_note IS NOT NULL AND length(bounded_note) > 0) OR (event_type = 'EvidenceRecorded' AND from_status IS NULL AND to_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL AND bounded_note IS NOT NULL AND length(bounded_note) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event",
                sql: "event_type IN ('Opened', 'StatusChanged', 'Reassigned', 'NoteAdded', 'EvidenceRecorded')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_acknowledged_fields",
                schema: "web_health",
                table: "incident",
                sql: "(status = 'Open' AND acknowledged_at IS NULL) OR (status IN ('Acknowledged', 'InProgress') AND acknowledged_at IS NOT NULL) OR status IN ('MonitoringRecovery', 'Resolved', 'Closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_durations",
                schema: "web_health",
                table: "incident",
                sql: "(recovery_duration_ms IS NULL OR recovery_duration_ms >= 0) AND (outage_duration_ms IS NULL OR outage_duration_ms >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_lifecycle_order",
                schema: "web_health",
                table: "incident",
                sql: "(acknowledged_at IS NULL OR acknowledged_at >= opened_at) AND (recovery_started_at IS NULL OR recovery_started_at >= opened_at) AND (resolved_at IS NULL OR resolved_at >= opened_at) AND (closed_at IS NULL OR (resolved_at IS NOT NULL AND closed_at >= resolved_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_recovery_fields",
                schema: "web_health",
                table: "incident",
                sql: "(status = 'MonitoringRecovery' AND recovery_started_at IS NOT NULL) OR (status IN ('Resolved', 'Closed')) OR (status IN ('Open', 'Acknowledged', 'InProgress') AND recovery_started_at IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_incident_evidence_app_user_actor_user_id",
                schema: "web_health",
                table: "incident_evidence",
                column: "actor_user_id",
                principalSchema: "web_health",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_incident_evidence_app_user_actor_user_id",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropIndex(
                name: "ix_incident_evidence_actor_user_id",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_evidence_source",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_evidence_type",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_acknowledged_fields",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_durations",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_lifecycle_order",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_recovery_fields",
                schema: "web_health",
                table: "incident");

            migrationBuilder.Sql("""
                ALTER TABLE web_health.incident_evidence
                    DISABLE TRIGGER trg_incident_evidence_immutable;

                DELETE FROM web_health.incident_evidence
                WHERE evidence_type = 'Resolution' OR logical_check_id IS NULL;

                ALTER TABLE web_health.incident_evidence
                    ENABLE TRIGGER trg_incident_evidence_immutable;

                ALTER TABLE web_health.incident_event
                    DISABLE TRIGGER trg_incident_event_immutable;

                DELETE FROM web_health.incident_event
                WHERE event_type = 'EvidenceRecorded';

                ALTER TABLE web_health.incident_event
                    ENABLE TRIGGER trg_incident_event_immutable;

                UPDATE web_health.incident
                SET acknowledged_at = COALESCE(acknowledged_at, opened_at)
                WHERE status <> 'Open';
                """);

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                schema: "web_health",
                table: "incident_evidence");

            migrationBuilder.DropColumn(
                name: "outage_duration_ms",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropColumn(
                name: "recovery_duration_ms",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropColumn(
                name: "recovery_started_at",
                schema: "web_health",
                table: "incident");

            migrationBuilder.AlterColumn<Guid>(
                name: "logical_check_id",
                schema: "web_health",
                table: "incident_evidence",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_evidence_type",
                schema: "web_health",
                table: "incident_evidence",
                sql: "evidence_type IN ('Opening', 'Failure', 'Recovery')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event",
                sql: "(event_type = 'Opened' AND to_status IS NOT NULL AND from_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL) OR (event_type = 'StatusChanged' AND from_status IS NOT NULL AND to_status IS NOT NULL AND from_status <> to_status AND from_status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') AND to_status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL) OR (event_type = 'Reassigned' AND from_status IS NULL AND to_status IS NULL AND to_owner_subject_id IS NOT NULL AND (from_owner_subject_id IS NULL OR from_owner_subject_id <> to_owner_subject_id)) OR (event_type = 'NoteAdded' AND from_status IS NULL AND to_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL AND bounded_note IS NOT NULL AND length(bounded_note) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event",
                sql: "event_type IN ('Opened', 'StatusChanged', 'Reassigned', 'NoteAdded')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_acknowledged_fields",
                schema: "web_health",
                table: "incident",
                sql: "(status = 'Open' AND acknowledged_at IS NULL) OR (status <> 'Open' AND acknowledged_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_lifecycle_order",
                schema: "web_health",
                table: "incident",
                sql: "(acknowledged_at IS NULL OR acknowledged_at >= opened_at) AND (resolved_at IS NULL OR (acknowledged_at IS NOT NULL AND resolved_at >= acknowledged_at)) AND (closed_at IS NULL OR (resolved_at IS NOT NULL AND closed_at >= resolved_at))");
        }
    }
}

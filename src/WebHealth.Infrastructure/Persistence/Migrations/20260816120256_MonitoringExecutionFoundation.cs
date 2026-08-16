using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringExecutionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE web_health.endpoint_monitor
                SET schedule_anchor = COALESCE(schedule_anchor, created_at),
                    next_due_at = COALESCE(next_due_at, created_at)
                WHERE schedule_anchor IS NULL OR next_due_at IS NULL;

                SET CONSTRAINTS ALL IMMEDIATE;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_monitor_threshold_order",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "schedule_anchor",
                schema: "web_health",
                table: "endpoint_monitor",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "next_due_at",
                schema: "web_health",
                table: "endpoint_monitor",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "logical_check",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cadence_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    policy_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logical_check", x => x.id);
                    table.UniqueConstraint("ak_logical_check_id_endpoint_monitor_id", x => new { x.id, x.endpoint_monitor_id });
                    table.CheckConstraint("ck_logical_check_policy_fingerprint", "length(policy_fingerprint) = 64");
                    table.CheckConstraint("ck_logical_check_source", "source IN ('Scheduled', 'Manual', 'Urgent')");
                    table.CheckConstraint("ck_logical_check_source_fields", "(source = 'Scheduled' AND scheduled_for IS NOT NULL AND cadence_key IS NOT NULL AND requested_at IS NULL AND initiated_by_user_id IS NULL) OR (source = 'Manual' AND scheduled_for IS NULL AND cadence_key IS NULL AND requested_at IS NOT NULL AND initiated_by_user_id IS NOT NULL) OR (source = 'Urgent' AND scheduled_for IS NULL AND cadence_key IS NULL AND requested_at IS NOT NULL)");
                    table.CheckConstraint("ck_logical_check_state", "state IN ('Pending', 'Queued', 'Running', 'Completed')");
                    table.CheckConstraint("ck_logical_check_timestamps", "(queued_at IS NULL OR queued_at >= created_at) AND (started_at IS NULL OR (queued_at IS NOT NULL AND started_at >= queued_at)) AND (completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at))");
                    table.ForeignKey(
                        name: "fk_logical_check_app_user_initiated_by_user_id",
                        column: x => x.initiated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_logical_check_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "check_configuration_snapshot",
                schema: "web_health",
                columns: table => new
                {
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    monitor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    configuration_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    failure_confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    recovery_confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    warning_threshold_ms = table.Column<int>(type: "integer", nullable: true),
                    critical_threshold_ms = table.Column<int>(type: "integer", nullable: true),
                    interval_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    timeout_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    confirmation_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    threshold_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_configuration_snapshot", x => x.logical_check_id);
                    table.CheckConstraint("ck_check_configuration_snapshot_fingerprint", "length(configuration_fingerprint) = 64");
                    table.CheckConstraint("ck_check_configuration_snapshot_positive_values", "interval_seconds > 0 AND timeout_seconds > 0 AND failure_confirmation_count > 0 AND recovery_confirmation_count > 0");
                    table.CheckConstraint("ck_check_configuration_snapshot_schema_version", "schema_version > 0");
                    table.CheckConstraint("ck_check_configuration_snapshot_sources", "interval_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride') AND timeout_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride') AND confirmation_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride') AND threshold_source IN ('SystemDefault', 'EnvironmentDefault', 'PolicyProfile', 'EndpointOverride')");
                    table.CheckConstraint("ck_check_configuration_snapshot_threshold_order", "(warning_threshold_ms IS NULL OR warning_threshold_ms >= 0) AND (critical_threshold_ms IS NULL OR critical_threshold_ms >= 0) AND (warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms < critical_threshold_ms)");
                    table.ForeignKey(
                        name: "fk_check_configuration_snapshot_logical_check_logical_check_id",
                        column: x => x.logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "durable_work",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    queue_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_failure_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_failure_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_durable_work", x => x.id);
                    table.CheckConstraint("ck_durable_work_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_durable_work_lease_fields", "(lease_owner_token IS NULL AND lease_acquired_at IS NULL AND lease_expires_at IS NULL) OR (lease_owner_token IS NOT NULL AND lease_acquired_at IS NOT NULL AND lease_expires_at IS NOT NULL AND lease_expires_at > lease_acquired_at)");
                    table.CheckConstraint("ck_durable_work_state", "state IN ('Pending', 'Enqueued', 'Processing', 'Completed', 'Failed')");
                    table.CheckConstraint("ck_durable_work_updated", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_durable_work_logical_check_logical_check_id",
                        column: x => x.logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_attempt",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    worker_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    infrastructure_outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    failure_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_attempt", x => x.id);
                    table.CheckConstraint("ck_execution_attempt_finished", "finished_at IS NULL OR finished_at >= started_at");
                    table.CheckConstraint("ck_execution_attempt_number", "attempt_number > 0");
                    table.CheckConstraint("ck_execution_attempt_outcome", "infrastructure_outcome IN ('Running', 'Succeeded', 'RetryableFailure', 'TerminalFailure', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_execution_attempt_logical_check_logical_check_id",
                        column: x => x.logical_check_id,
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_lease",
                schema: "web_health",
                columns: table => new
                {
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_token = table.Column<Guid>(type: "uuid", nullable: false),
                    fencing_generation = table.Column<long>(type: "bigint", nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_lease", x => x.endpoint_monitor_id);
                    table.CheckConstraint("ck_execution_lease_expiry", "expires_at > acquired_at");
                    table.CheckConstraint("ck_execution_lease_generation", "fencing_generation > 0");
                    table.ForeignKey(
                        name: "fk_execution_lease_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_lease_logical_check_monitor",
                        columns: x => new { x.logical_check_id, x.endpoint_monitor_id },
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumns: new[] { "id", "endpoint_monitor_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_next_due_at_id",
                schema: "web_health",
                table: "endpoint_monitor",
                columns: new[] { "next_due_at", "id" },
                filter: "deleted_at IS NULL AND is_enabled");

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_monitor_threshold_order",
                schema: "web_health",
                table: "endpoint_monitor",
                sql: "(warning_threshold_ms IS NULL OR warning_threshold_ms >= 0) AND (critical_threshold_ms IS NULL OR critical_threshold_ms >= 0) AND (warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms < critical_threshold_ms)");

            migrationBuilder.CreateIndex(
                name: "ix_durable_work_logical_check_id",
                schema: "web_health",
                table: "durable_work",
                column: "logical_check_id");

            migrationBuilder.CreateIndex(
                name: "ix_durable_work_state_available_at",
                schema: "web_health",
                table: "durable_work",
                columns: new[] { "state", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_durable_work_work_kind_dedupe_key",
                schema: "web_health",
                table: "durable_work",
                columns: new[] { "work_kind", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_attempt_logical_check_id_attempt_number",
                schema: "web_health",
                table: "execution_attempt",
                columns: new[] { "logical_check_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_lease_expires_at",
                schema: "web_health",
                table: "execution_lease",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_execution_lease_logical_check_id_endpoint_monitor_id",
                schema: "web_health",
                table: "execution_lease",
                columns: new[] { "logical_check_id", "endpoint_monitor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_logical_check_endpoint_monitor_id_cadence_key",
                schema: "web_health",
                table: "logical_check",
                columns: new[] { "endpoint_monitor_id", "cadence_key" },
                unique: true,
                filter: "source = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_logical_check_endpoint_monitor_id_created_at",
                schema: "web_health",
                table: "logical_check",
                columns: new[] { "endpoint_monitor_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_logical_check_initiated_by_user_id",
                schema: "web_health",
                table: "logical_check",
                column: "initiated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_logical_check_state_created_at",
                schema: "web_health",
                table: "logical_check",
                columns: new[] { "state", "created_at" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION web_health.enforce_logical_check_snapshot()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW.state <> 'Pending'
                       AND NOT EXISTS (
                           SELECT 1
                           FROM web_health.check_configuration_snapshot snapshot
                           WHERE snapshot.logical_check_id = NEW.id)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_logical_check_nonpending_snapshot',
                            MESSAGE = 'A configuration snapshot is required before a logical check leaves Pending.';
                    END IF;
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_logical_check_nonpending_snapshot
                AFTER INSERT OR UPDATE OF state ON web_health.logical_check
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_logical_check_snapshot();

                CREATE FUNCTION web_health.reject_check_configuration_snapshot_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'check_configuration_snapshot rows are immutable';
                END;
                $function$;

                CREATE TRIGGER trg_check_configuration_snapshot_immutable
                BEFORE UPDATE OR DELETE ON web_health.check_configuration_snapshot
                FOR EACH ROW
                EXECUTE FUNCTION web_health.reject_check_configuration_snapshot_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS ck_logical_check_nonpending_snapshot
                    ON web_health.logical_check;
                DROP FUNCTION IF EXISTS web_health.enforce_logical_check_snapshot();
                DROP TRIGGER IF EXISTS trg_check_configuration_snapshot_immutable
                    ON web_health.check_configuration_snapshot;
                DROP FUNCTION IF EXISTS web_health.reject_check_configuration_snapshot_mutation();
                """);

            migrationBuilder.DropTable(
                name: "check_configuration_snapshot",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "durable_work",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "execution_attempt",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "execution_lease",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "logical_check",
                schema: "web_health");

            migrationBuilder.DropIndex(
                name: "ix_endpoint_monitor_next_due_at_id",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_monitor_threshold_order",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "schedule_anchor",
                schema: "web_health",
                table: "endpoint_monitor",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "next_due_at",
                schema: "web_health",
                table: "endpoint_monitor",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_monitor_threshold_order",
                schema: "web_health",
                table: "endpoint_monitor",
                sql: "warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms < critical_threshold_ms");
        }
    }
}

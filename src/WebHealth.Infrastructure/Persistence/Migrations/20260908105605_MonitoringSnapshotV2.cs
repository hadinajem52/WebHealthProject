using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class MonitoringSnapshotV2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_schema_version",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.AddColumn<long>(
                name: "current_truth_generation",
                schema: "web_health",
                table: "endpoint_monitor",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "current_state_disposition",
                schema: "web_health",
                table: "check_result",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Current");

            migrationBuilder.AddColumn<long>(
                name: "current_truth_generation",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_effective_port",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "target_is_production",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_normalization_version",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_normalized_host",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_normalized_url",
                schema: "web_health",
                table: "check_configuration_snapshot",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_endpoint_monitor_current_truth_generation",
                schema: "web_health",
                table: "endpoint_monitor",
                sql: "current_truth_generation > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_current_state_disposition",
                schema: "web_health",
                table: "check_result",
                sql: "current_state_disposition IN ('Current', 'Superseded', 'Ineligible')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_schema_version",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "schema_version IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_v2_target",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "schema_version = 1 OR (target_normalized_url IS NOT NULL AND length(target_normalized_url) > 0 AND target_normalized_host IS NOT NULL AND length(target_normalized_host) > 0 AND target_effective_port IS NOT NULL AND target_effective_port BETWEEN 1 AND 65535 AND target_normalization_version IS NOT NULL AND target_normalization_version > 0 AND target_is_production IS NOT NULL AND current_truth_generation IS NOT NULL AND current_truth_generation > 0)");
            migrationBuilder.Sql("""
                CREATE FUNCTION web_health.advance_monitor_truth() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(OLD.endpoint_id, OLD.monitor_type, OLD.policy_profile_id, OLD.configuration_fingerprint, OLD.bounded_overrides, OLD.interval_seconds, OLD.timeout_seconds, OLD.failure_confirmation_count, OLD.recovery_confirmation_count, OLD.warning_threshold_ms, OLD.critical_threshold_ms, OLD.is_enabled, OLD.scheduling_enabled, OLD.deleted_at) IS DISTINCT FROM ROW(NEW.endpoint_id, NEW.monitor_type, NEW.policy_profile_id, NEW.configuration_fingerprint, NEW.bounded_overrides, NEW.interval_seconds, NEW.timeout_seconds, NEW.failure_confirmation_count, NEW.recovery_confirmation_count, NEW.warning_threshold_ms, NEW.critical_threshold_ms, NEW.is_enabled, NEW.scheduling_enabled, NEW.deleted_at) THEN
                        NEW.current_truth_generation := OLD.current_truth_generation + 1;
                    ELSIF NEW.current_truth_generation < OLD.current_truth_generation THEN
                        RAISE EXCEPTION 'Monitor generation cannot decrease';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER endpoint_monitor_truth BEFORE UPDATE ON web_health.endpoint_monitor
                FOR EACH ROW EXECUTE FUNCTION web_health.advance_monitor_truth();
                CREATE FUNCTION web_health.advance_endpoint_monitor_truth() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(OLD.normalized_url, OLD.normalized_host, OLD.effective_port, OLD.normalization_version, OLD.is_enabled, OLD.deleted_at, OLD.environment_id) IS DISTINCT FROM ROW(NEW.normalized_url, NEW.normalized_host, NEW.effective_port, NEW.normalization_version, NEW.is_enabled, NEW.deleted_at, NEW.environment_id) THEN
                        UPDATE web_health.endpoint_monitor m
                        SET current_truth_generation = current_truth_generation + 1
                        WHERE m.deleted_at IS NULL AND m.endpoint_id = NEW.id;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER endpoint_monitor_truth AFTER UPDATE ON web_health.endpoint
                FOR EACH ROW EXECUTE FUNCTION web_health.advance_endpoint_monitor_truth();
                CREATE FUNCTION web_health.advance_environment_monitor_truth() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(OLD.is_active, OLD.is_production, OLD.deleted_at, OLD.website_id) IS DISTINCT FROM ROW(NEW.is_active, NEW.is_production, NEW.deleted_at, NEW.website_id) THEN
                        UPDATE web_health.endpoint_monitor m
                        SET current_truth_generation = current_truth_generation + 1
                        WHERE m.deleted_at IS NULL AND m.endpoint_id IN (SELECT id FROM web_health.endpoint WHERE environment_id = NEW.id);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER environment_monitor_truth AFTER UPDATE ON web_health.environment
                FOR EACH ROW EXECUTE FUNCTION web_health.advance_environment_monitor_truth();
                CREATE FUNCTION web_health.advance_website_monitor_truth() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(OLD.is_enabled, OLD.deleted_at, OLD.client_id) IS DISTINCT FROM ROW(NEW.is_enabled, NEW.deleted_at, NEW.client_id) THEN
                        UPDATE web_health.endpoint_monitor m
                        SET current_truth_generation = current_truth_generation + 1
                        WHERE m.deleted_at IS NULL AND m.endpoint_id IN (SELECT e.id FROM web_health.endpoint e JOIN web_health.environment v ON v.id = e.environment_id WHERE v.website_id = NEW.id);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER website_monitor_truth AFTER UPDATE ON web_health.website
                FOR EACH ROW EXECUTE FUNCTION web_health.advance_website_monitor_truth();
                CREATE FUNCTION web_health.advance_client_monitor_truth() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(OLD.is_active, OLD.deleted_at) IS DISTINCT FROM ROW(NEW.is_active, NEW.deleted_at) THEN
                        UPDATE web_health.endpoint_monitor m
                        SET current_truth_generation = current_truth_generation + 1
                        WHERE m.deleted_at IS NULL AND m.endpoint_id IN (SELECT e.id FROM web_health.endpoint e JOIN web_health.environment v ON v.id = e.environment_id JOIN web_health.website w ON w.id = v.website_id WHERE w.client_id = NEW.id);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER client_monitor_truth AFTER UPDATE ON web_health.client
                FOR EACH ROW EXECUTE FUNCTION web_health.advance_client_monitor_truth();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO web_health.check_result
                    (logical_check_id, endpoint_monitor_id, outcome, failure_category, total_duration_ms,
                     response_truncated, monitor_source, measured_at, is_maintenance, counts_for_uptime,
                     safe_diagnostic, completed_at, current_state_disposition)
                SELECT c.id, c.endpoint_monitor_id, 'Cancelled', 'Cancellation', 0, FALSE, c.source,
                       GREATEST(now(), c.started_at, c.queued_at, c.created_at), FALSE, FALSE,
                       'Cancelled by snapshot schema rollback.',
                       GREATEST(now(), c.started_at, c.queued_at, c.created_at), 'Ineligible'
                FROM web_health.logical_check c
                JOIN web_health.check_configuration_snapshot s ON s.logical_check_id = c.id
                WHERE s.schema_version = 2 AND c.state <> 'Completed'
                ON CONFLICT (logical_check_id) DO NOTHING;
                UPDATE web_health.durable_work w
                SET state = 'Completed', updated_at = GREATEST(now(), w.updated_at),
                    lease_owner_token = NULL, lease_acquired_at = NULL, lease_expires_at = NULL
                FROM web_health.check_configuration_snapshot s
                WHERE s.logical_check_id = w.logical_check_id AND s.schema_version = 2;
                UPDATE web_health.execution_attempt a
                SET infrastructure_outcome = 'Cancelled', finished_at = GREATEST(now(), a.started_at),
                    failure_category = 'SnapshotRollback'
                FROM web_health.check_configuration_snapshot s
                WHERE s.logical_check_id = a.logical_check_id AND s.schema_version = 2
                    AND a.infrastructure_outcome = 'Running';
                DELETE FROM web_health.execution_lease l USING web_health.check_configuration_snapshot s
                WHERE s.logical_check_id = l.logical_check_id AND s.schema_version = 2;
                UPDATE web_health.logical_check c
                SET state = 'Completed', queued_at = COALESCE(c.queued_at, c.created_at),
                    started_at = COALESCE(c.started_at, c.queued_at, c.created_at),
                    completed_at = GREATEST(now(), c.started_at, c.queued_at, c.created_at)
                FROM web_health.check_configuration_snapshot s
                WHERE s.logical_check_id = c.id AND s.schema_version = 2 AND c.state <> 'Completed';
                SET CONSTRAINTS ALL IMMEDIATE;
                ALTER TABLE web_health.check_configuration_snapshot
                    DISABLE TRIGGER trg_check_configuration_snapshot_immutable;
                UPDATE web_health.check_configuration_snapshot SET schema_version = 1 WHERE schema_version = 2;
                ALTER TABLE web_health.check_configuration_snapshot
                    ENABLE TRIGGER trg_check_configuration_snapshot_immutable;
                DROP TRIGGER client_monitor_truth ON web_health.client;
                DROP FUNCTION web_health.advance_client_monitor_truth();
                DROP TRIGGER website_monitor_truth ON web_health.website;
                DROP FUNCTION web_health.advance_website_monitor_truth();
                DROP TRIGGER environment_monitor_truth ON web_health.environment;
                DROP FUNCTION web_health.advance_environment_monitor_truth();
                DROP TRIGGER endpoint_monitor_truth ON web_health.endpoint;
                DROP FUNCTION web_health.advance_endpoint_monitor_truth();
                DROP TRIGGER endpoint_monitor_truth ON web_health.endpoint_monitor;
                DROP FUNCTION web_health.advance_monitor_truth();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_endpoint_monitor_current_truth_generation",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_current_state_disposition",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_schema_version",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_configuration_snapshot_v2_target",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "current_truth_generation",
                schema: "web_health",
                table: "endpoint_monitor");

            migrationBuilder.DropColumn(
                name: "current_state_disposition",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropColumn(
                name: "current_truth_generation",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "target_effective_port",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "target_is_production",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "target_normalization_version",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "target_normalized_host",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.DropColumn(
                name: "target_normalized_url",
                schema: "web_health",
                table: "check_configuration_snapshot");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_configuration_snapshot_schema_version",
                schema: "web_health",
                table: "check_configuration_snapshot",
                sql: "schema_version > 0");
        }
    }
}

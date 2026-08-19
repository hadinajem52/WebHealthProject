using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringMaintenanceOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The previous key was (window, starts_at, ends_at), so two rows could legally share a
            // start with different ends. Detect that before the narrower unique index is built, and
            // stop with an actionable message: these rows cannot be deleted automatically because
            // check_result.maintenance_occurrence_id may reference either of them.
            migrationBuilder.Sql("""
                DO $$
                DECLARE conflicting int;
                BEGIN
                    SELECT count(*) INTO conflicting FROM (
                        SELECT maintenance_window_id, starts_at
                        FROM web_health.maintenance_occurrence
                        GROUP BY maintenance_window_id, starts_at
                        HAVING count(*) > 1
                    ) AS duplicates;

                    IF conflicting > 0 THEN
                        RAISE EXCEPTION
                            'RecurringMaintenanceOccurrences cannot apply: % (window, starts_at) pairs have more than one occurrence. Resolve them by hand, keeping the row referenced by check_result.', conflicting;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_maintenance_occurrence_window_interval",
                schema: "web_health",
                table: "maintenance_occurrence");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expanded_through",
                schema: "web_health",
                table: "maintenance_window",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recurrence_days_of_week",
                schema: "web_health",
                table: "maintenance_window",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "recurrence_pattern",
                schema: "web_health",
                table: "maintenance_window",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recurrence_until",
                schema: "web_health",
                table: "maintenance_window",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schedule_duration_seconds",
                schema: "web_health",
                table: "maintenance_window",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "schedule_starts_at",
                schema: "web_health",
                table: "maintenance_window",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing windows are one-off and already materialised. Their schedule specification is
            // recovered from the occurrence they own, before the duration check constraint is added.
            migrationBuilder.Sql("""
                UPDATE web_health.maintenance_window AS w
                SET schedule_starts_at = o.starts_at,
                    schedule_duration_seconds = EXTRACT(EPOCH FROM (o.ends_at - o.starts_at))::int
                FROM (
                    SELECT DISTINCT ON (maintenance_window_id)
                        maintenance_window_id, starts_at, ends_at
                    FROM web_health.maintenance_occurrence
                    ORDER BY maintenance_window_id, starts_at
                ) AS o
                WHERE o.maintenance_window_id = w.id;
                """);

            // A window with no occurrence has no schedule to recover and could never suppress
            // anything. Fail rather than invent one: it means the row predates a rule this schema
            // now relies on, and only a person can decide what it was meant to be.
            migrationBuilder.Sql("""
                DO $$
                DECLARE orphaned int;
                BEGIN
                    SELECT count(*) INTO orphaned
                    FROM web_health.maintenance_window AS w
                    WHERE NOT EXISTS (
                        SELECT 1 FROM web_health.maintenance_occurrence AS o
                        WHERE o.maintenance_window_id = w.id);

                    IF orphaned > 0 THEN
                        RAISE EXCEPTION
                            'RecurringMaintenanceOccurrences cannot apply: % maintenance windows have no occurrence and therefore no recoverable schedule.', orphaned;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_window_recurrence_expansion",
                schema: "web_health",
                table: "maintenance_window",
                columns: new[] { "recurrence_pattern", "expanded_through" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_window_recurrence",
                schema: "web_health",
                table: "maintenance_window",
                sql: "(recurrence_pattern = 'None' AND recurrence_days_of_week = 0 AND recurrence_until IS NULL) OR (recurrence_pattern = 'Daily' AND recurrence_days_of_week = 0) OR (recurrence_pattern = 'Weekly' AND recurrence_days_of_week BETWEEN 1 AND 127)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_window_recurrence_until",
                schema: "web_health",
                table: "maintenance_window",
                sql: "recurrence_until IS NULL OR recurrence_until > schedule_starts_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_window_schedule_duration",
                schema: "web_health",
                table: "maintenance_window",
                sql: "schedule_duration_seconds > 0");

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_occurrence_window_start",
                schema: "web_health",
                table: "maintenance_occurrence",
                columns: new[] { "maintenance_window_id", "starts_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_maintenance_window_recurrence_expansion",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_window_recurrence",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_window_recurrence_until",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_window_schedule_duration",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropIndex(
                name: "ux_maintenance_occurrence_window_start",
                schema: "web_health",
                table: "maintenance_occurrence");

            migrationBuilder.DropColumn(
                name: "expanded_through",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropColumn(
                name: "recurrence_days_of_week",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropColumn(
                name: "recurrence_pattern",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropColumn(
                name: "recurrence_until",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropColumn(
                name: "schedule_duration_seconds",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.DropColumn(
                name: "schedule_starts_at",
                schema: "web_health",
                table: "maintenance_window");

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_occurrence_window_interval",
                schema: "web_health",
                table: "maintenance_occurrence",
                columns: new[] { "maintenance_window_id", "starts_at", "ends_at" },
                unique: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Four evidence tables are protected by <c>BEFORE UPDATE OR DELETE</c> triggers that reject
    /// every mutation. The intent behind them is that recorded evidence can never be silently
    /// rewritten; rejecting deletes came along with that and made the rows permanently
    /// undeletable, which leaves an endpoint purge unable to finish.
    /// </summary>
    /// <remarks>
    /// The rewrite below keeps the rejection for updates exactly as it was and opens one door for
    /// deletes: the transaction must have set <c>web_health.endpoint_purge</c> to <c>on</c>. Only
    /// the endpoint purge sets it, and it sets it with <c>SET LOCAL</c>, so the exemption cannot
    /// outlive the transaction that opened it. An ordinary delete against any of these tables is
    /// rejected exactly as before.
    /// <para>
    /// The alternative - dropping <c>OR DELETE</c> from the triggers - was rejected because it
    /// would leave a stray <c>DELETE FROM incident_event</c> succeeding silently, which is the
    /// thing the trigger exists to prevent.
    /// </para>
    /// </remarks>
    public partial class EndpointPurgeEvidenceRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION web_health.reject_check_configuration_snapshot_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE'
                        AND coalesce(current_setting('web_health.endpoint_purge', TRUE), '') = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'check_configuration_snapshot rows are immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.reject_incident_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE'
                        AND coalesce(current_setting('web_health.endpoint_purge', TRUE), '') = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'incident_event rows are immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.reject_incident_evidence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE'
                        AND coalesce(current_setting('web_health.endpoint_purge', TRUE), '') = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'incident_evidence rows are immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.reject_maintenance_occurrence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE'
                        AND coalesce(current_setting('web_health.endpoint_purge', TRUE), '') = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'maintenance_occurrence rows are immutable';
                END;
                $function$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION web_health.reject_check_configuration_snapshot_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'check_configuration_snapshot rows are immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.reject_incident_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'incident_event rows are immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.reject_incident_evidence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'incident_evidence rows are immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION web_health.reject_maintenance_occurrence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'maintenance_occurrence rows are immutable';
                END;
                $function$;
                """);
        }
    }
}

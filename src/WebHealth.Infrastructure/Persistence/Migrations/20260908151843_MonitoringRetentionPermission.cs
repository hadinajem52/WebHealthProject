using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class MonitoringRetentionPermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReplaceFunctions(migrationBuilder, true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ReplaceFunctions(migrationBuilder, false);
        }

        private static void ReplaceFunctions(MigrationBuilder migrationBuilder, bool allowRetention)
        {
            var retentionPermission = allowRetention
                ? "OR coalesce(current_setting('web_health.monitoring_retention', TRUE), '') = 'on'"
                : string.Empty;
            foreach (var table in new[] { "check_configuration_snapshot", "incident_event", "incident_evidence" })
                migrationBuilder.Sql($"""
                    CREATE OR REPLACE FUNCTION web_health.reject_{table}_mutation()
                    RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        IF TG_OP = 'DELETE' AND
                            (coalesce(current_setting('web_health.endpoint_purge', TRUE), '') = 'on'
                                {retentionPermission}) THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION '{table} rows are immutable';
                    END;
                    $$;
                    """);
        }
    }
}

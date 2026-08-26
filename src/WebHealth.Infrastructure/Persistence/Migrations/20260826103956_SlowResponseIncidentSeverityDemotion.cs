using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlowResponseIncidentSeverityDemotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                UPDATE web_health.incident
                SET severity = 'Warning', version = version + 1
                WHERE severity <> 'Warning'
                  AND status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery')
                  AND issue_key LIKE 'v1|HttpAvailability|Http.SlowResponse|%';
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

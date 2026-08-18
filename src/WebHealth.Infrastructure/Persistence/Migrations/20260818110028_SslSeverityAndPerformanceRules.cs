using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SslSeverityAndPerformanceRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_severity",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finding_severity",
                schema: "web_health",
                table: "finding");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event",
                sql: "(event_type = 'Opened' AND to_status IS NOT NULL AND from_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL) OR (event_type = 'StatusChanged' AND from_status IS NOT NULL AND to_status IS NOT NULL AND from_status <> to_status AND from_status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') AND to_status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL) OR (event_type = 'Reassigned' AND from_status IS NULL AND to_status IS NULL AND to_owner_subject_id IS NOT NULL AND (from_owner_subject_id IS NULL OR from_owner_subject_id <> to_owner_subject_id)) OR (event_type = 'NoteAdded' AND from_status IS NULL AND to_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL AND bounded_note IS NOT NULL AND length(bounded_note) > 0) OR (event_type = 'EvidenceRecorded' AND from_status IS NULL AND to_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL AND bounded_note IS NOT NULL AND length(bounded_note) > 0) OR (event_type = 'CertificateRenewed' AND from_status IS NULL AND to_status IS NULL AND from_owner_subject_id IS NULL AND to_owner_subject_id IS NULL AND bounded_note IS NOT NULL AND length(bounded_note) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event",
                sql: "event_type IN ('Opened', 'StatusChanged', 'Reassigned', 'NoteAdded', 'EvidenceRecorded', 'CertificateRenewed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_severity",
                schema: "web_health",
                table: "incident",
                sql: "severity IN ('Warning', 'High', 'Critical')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_finding_severity",
                schema: "web_health",
                table: "finding",
                sql: "severity IN ('Warning', 'High', 'Critical')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol','SlowResponse','PageTooLarge','SslExpired','SslNotYetValid','SslHostnameMismatch','SslUntrusted','SslHandshakeFailed','SslExpiringSoon')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_fields",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_type",
                schema: "web_health",
                table: "incident_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_severity",
                schema: "web_health",
                table: "incident");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finding_severity",
                schema: "web_health",
                table: "finding");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

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
                name: "ck_incident_severity",
                schema: "web_health",
                table: "incident",
                sql: "severity IN ('Warning', 'Critical')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_finding_severity",
                schema: "web_health",
                table: "finding",
                sql: "severity IN ('Warning', 'Critical')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol','SslExpired','SslNotYetValid','SslHostnameMismatch','SslUntrusted','SslHandshakeFailed')");
        }
    }
}

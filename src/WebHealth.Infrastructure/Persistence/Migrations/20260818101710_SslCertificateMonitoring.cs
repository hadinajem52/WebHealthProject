using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SslCertificateMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.CreateTable(
                name: "certificate_observation",
                schema: "web_health",
                columns: table => new
                {
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sha256_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    days_remaining = table.Column<int>(type: "integer", nullable: false),
                    validation_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    hostname_matched = table.Column<bool>(type: "boolean", nullable: false),
                    chain_trusted = table.Column<bool>(type: "boolean", nullable: false),
                    subject_alternative_names = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_observation", x => x.logical_check_id);
                    table.CheckConstraint("ck_certificate_observation_category", "validation_category IN ('Valid', 'NotYetValid', 'Expired', 'HostnameMismatch', 'Untrusted')");
                    table.CheckConstraint("ck_certificate_observation_fingerprint", "sha256_fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_certificate_observation_validity_window", "not_after >= not_before");
                    table.ForeignKey(
                        name: "fk_certificate_observation_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_certificate_observation_logical_check_logical_check_id_endp~",
                        columns: x => new { x.logical_check_id, x.endpoint_monitor_id },
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumns: new[] { "id", "endpoint_monitor_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "web_health",
                table: "policy_profile",
                columns: new[] { "id", "bounded_settings", "created_at", "deleted_at", "is_system", "monitor_type", "name", "version" },
                values: new object[] { new Guid("0d6d3f5c-4a1b-4d2e-9f30-6b8c5a2d71e4"), "{}", new DateTimeOffset(new DateTime(2026, 8, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "SslCertificate", "Default SSL certificate", 1L });

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol','SslExpired','SslNotYetValid','SslHostnameMismatch','SslUntrusted','SslHandshakeFailed')");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_observation_endpoint_monitor_id_observed_at",
                schema: "web_health",
                table: "certificate_observation",
                columns: new[] { "endpoint_monitor_id", "observed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_certificate_observation_logical_check_id_endpoint_monitor_id",
                schema: "web_health",
                table: "certificate_observation",
                columns: new[] { "logical_check_id", "endpoint_monitor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificate_observation_sha256_fingerprint",
                schema: "web_health",
                table: "certificate_observation",
                column: "sha256_fingerprint");

            BackfillSslMonitors(migrationBuilder);
        }

        /// <summary>
        /// Gives every existing HTTPS endpoint the certificate monitor it would have been
        /// created with (BR-C01). Without this, only endpoints created or edited after this
        /// migration would ever be certificate-monitored.
        ///
        /// The fingerprint is the same canonical v2 policy string the application computes in
        /// <c>HttpPolicyFingerprint</c>: each field is written as its UTF-8 byte length, a
        /// colon, the value and a pipe, with null written as "-1:". It is reproduced here
        /// rather than approximated because dispatch rejects any check whose stored fingerprint
        /// does not match the recomputed one, and a mismatch would silently disable every
        /// backfilled monitor.
        /// </summary>
        private static void BackfillSslMonitors(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                INSERT INTO web_health.endpoint_monitor (
                    id, endpoint_id, policy_profile_id, monitor_type, bounded_overrides,
                    schedule_anchor, next_due_at, configuration_fingerprint,
                    interval_seconds, timeout_seconds,
                    failure_confirmation_count, recovery_confirmation_count,
                    warning_threshold_ms, critical_threshold_ms,
                    scheduling_enabled, is_enabled,
                    created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                SELECT
                    gen_random_uuid(),
                    endpoint.id,
                    '0d6d3f5c-4a1b-4d2e-9f30-6b8c5a2d71e4'::uuid,
                    'SslCertificate',
                    '{}'::jsonb,
                    now(),
                    now(),
                    encode(sha256(convert_to(
                        'v2|'
                        || octet_length(endpoint.normalized_url)::text || ':'
                        || endpoint.normalized_url || '|'
                        || '14:SslCertificate|'
                        || '1:' || CASE WHEN environment.is_production THEN '1' ELSE '0' END || '|'
                        || '5:86400|2:15|1:1|1:1|-1:|-1:|0:|-1:|'
                        || '17:OrdinalIgnoreCase|7:Warning|7:2097152|2:10|',
                        'UTF8')), 'hex'),
                    86400, 15, 1, 1, NULL, NULL,
                    http_monitor.scheduling_enabled,
                    http_monitor.is_enabled,
                    now(), http_monitor.created_by_user_id, now(), http_monitor.updated_by_user_id, 1
                FROM web_health.endpoint AS endpoint
                JOIN web_health.environment AS environment ON environment.id = endpoint.environment_id
                JOIN web_health.endpoint_monitor AS http_monitor
                  ON http_monitor.endpoint_id = endpoint.id
                 AND http_monitor.monitor_type = 'HttpAvailability'
                 AND http_monitor.deleted_at IS NULL
                WHERE endpoint.deleted_at IS NULL
                  AND endpoint.normalized_url LIKE 'https://%'
                  AND NOT EXISTS (
                      SELECT 1 FROM web_health.endpoint_monitor AS existing
                      WHERE existing.endpoint_id = endpoint.id
                        AND existing.monitor_type = 'SslCertificate'
                        AND existing.deleted_at IS NULL);
                """);


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DESTRUCTIVE AND FORWARD-ONLY IN PRACTICE. This deletes every certificate
            // monitor, including ones the application created after the migration was applied,
            // and the table drop below discards all certificate observations. Rolling back is
            // therefore data loss, not a reversal: recover by restoring a backup or by writing
            // a forward migration.
            migrationBuilder.Sql(
                "DELETE FROM web_health.endpoint_monitor WHERE monitor_type = 'SslCertificate';");


            migrationBuilder.DropTable(
                name: "certificate_observation",
                schema: "web_health");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DeleteData(
                schema: "web_health",
                table: "policy_profile",
                keyColumn: "id",
                keyValue: new Guid("0d6d3f5c-4a1b-4d2e-9f30-6b8c5a2d71e4"));

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol')");
        }
    }
}

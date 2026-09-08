using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations;

public partial class SslPolicyFingerprint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => Rewrite(migrationBuilder, "legacy", "current");

    protected override void Down(MigrationBuilder migrationBuilder) => Rewrite(migrationBuilder, "current", "legacy");

    private static void Rewrite(MigrationBuilder migrationBuilder, string previous, string next) =>
        migrationBuilder.Sql($"""
            WITH payloads AS (
                SELECT m.id, octet_length(e.normalized_url)::text || ':' || e.normalized_url || '|14:SslCertificate|1:'
                    || CASE WHEN v.is_production THEN '1' ELSE '0' END || '|'
                    || length(m.interval_seconds::text)::text || ':' || m.interval_seconds::text || '|'
                    || length(m.timeout_seconds::text)::text || ':' || m.timeout_seconds::text || '|'
                    || length(m.failure_confirmation_count::text)::text || ':' || m.failure_confirmation_count::text || '|'
                    || length(m.recovery_confirmation_count::text)::text || ':' || m.recovery_confirmation_count::text || '|' AS payload
                FROM web_health.endpoint_monitor m
                JOIN web_health.endpoint e ON e.id = m.endpoint_id
                JOIN web_health.environment v ON v.id = e.environment_id
                WHERE m.monitor_type = 'SslCertificate' AND m.deleted_at IS NULL
            ), fingerprints AS (
                SELECT id,
                    encode(sha256(convert_to('v2|' || payload || '-1:|-1:|0:|-1:|17:OrdinalIgnoreCase|7:Warning|7:2097152|2:10|', 'UTF8')), 'hex') AS legacy,
                    encode(sha256(convert_to('ssl-v1|' || payload || '2:30|2:15|1:7|', 'UTF8')), 'hex') AS current
                FROM payloads
            )
            UPDATE web_health.endpoint_monitor m SET configuration_fingerprint = f.{next}
            FROM fingerprints f WHERE m.id = f.id AND m.configuration_fingerprint = f.{previous};
            """);
}

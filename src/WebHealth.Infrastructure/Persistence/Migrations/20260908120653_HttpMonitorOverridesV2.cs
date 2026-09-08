using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations;

public partial class HttpMonitorOverridesV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE web_health.endpoint_monitor m
            SET bounded_overrides = jsonb_strip_nulls(jsonb_build_object(
                'schemaVersion', 2,
                'intervalSeconds', CASE
                    WHEN m.bounded_overrides ? 'intervalSeconds' THEN m.interval_seconds
                    WHEN m.interval_seconds <> CASE WHEN v.is_production THEN 300 ELSE 900 END THEN m.interval_seconds
                    ELSE NULL END,
                'timeoutSeconds', m.timeout_seconds,
                'failureConfirmationCount', m.failure_confirmation_count,
                'recoveryConfirmationCount', m.recovery_confirmation_count,
                'warningThresholdMs', m.warning_threshold_ms,
                'criticalThresholdMs', m.critical_threshold_ms))
            FROM web_health.endpoint e JOIN web_health.environment v ON v.id = e.environment_id
            WHERE e.id = m.endpoint_id AND m.monitor_type = 'HttpAvailability' AND m.deleted_at IS NULL
                AND NOT (m.bounded_overrides ? 'schemaVersion');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                CREATE FUNCTION web_health.http_policy_fingerprint_v2(
                    p_url text,
                    p_monitor_type text,
                    p_is_production boolean,
                    p_interval integer,
                    p_timeout integer,
                    p_failure_count integer,
                    p_recovery_count integer,
                    p_warning integer,
                    p_critical integer,
                    p_statuses text,
                    p_marker text,
                    p_comparison text,
                    p_http_severity text,
                    p_body_limit integer,
                    p_redirect_limit integer)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                AS $function$
                    SELECT encode(sha256(convert_to(
                        'v2|' ||
                        octet_length(p_url)::text || ':' || p_url || '|' ||
                        octet_length(p_monitor_type)::text || ':' || p_monitor_type || '|' ||
                        '1:' || CASE WHEN p_is_production THEN '1' ELSE '0' END || '|' ||
                        octet_length(p_interval::text)::text || ':' || p_interval::text || '|' ||
                        octet_length(p_timeout::text)::text || ':' || p_timeout::text || '|' ||
                        octet_length(p_failure_count::text)::text || ':' || p_failure_count::text || '|' ||
                        octet_length(p_recovery_count::text)::text || ':' || p_recovery_count::text || '|' ||
                        CASE WHEN p_warning IS NULL THEN '-1:|' ELSE octet_length(p_warning::text)::text || ':' || p_warning::text || '|' END ||
                        CASE WHEN p_critical IS NULL THEN '-1:|' ELSE octet_length(p_critical::text)::text || ':' || p_critical::text || '|' END ||
                        octet_length(p_statuses)::text || ':' || p_statuses || '|' ||
                        CASE WHEN p_marker IS NULL THEN '-1:|' ELSE octet_length(p_marker)::text || ':' || p_marker || '|' END ||
                        octet_length(p_comparison)::text || ':' || p_comparison || '|' ||
                        octet_length(p_http_severity)::text || ':' || p_http_severity || '|' ||
                        octet_length(p_body_limit::text)::text || ':' || p_body_limit::text || '|' ||
                        octet_length(p_redirect_limit::text)::text || ':' || p_redirect_limit::text || '|',
                        'UTF8')), 'hex');
                $function$;
                UPDATE web_health.endpoint_monitor monitor
                SET configuration_fingerprint = web_health.http_policy_fingerprint_v2(
                    endpoint.normalized_url,
                    monitor.monitor_type,
                    environment.is_production,
                    monitor.interval_seconds,
                    monitor.timeout_seconds,
                    monitor.failure_confirmation_count,
                    monitor.recovery_confirmation_count,
                    monitor.warning_threshold_ms,
                    monitor.critical_threshold_ms,
                    '',
                    NULL,
                    'OrdinalIgnoreCase',
                    'Warning',
                    2097152,
                    10)
                FROM web_health.endpoint endpoint
                JOIN web_health.environment environment ON environment.id = endpoint.environment_id
                WHERE endpoint.id = monitor.endpoint_id AND monitor.monitor_type = 'HttpAvailability' AND monitor.deleted_at IS NULL;
                UPDATE web_health.endpoint_monitor
                SET bounded_overrides = CASE WHEN bounded_overrides ? 'intervalSeconds'
                    THEN jsonb_build_object('intervalSeconds', bounded_overrides -> 'intervalSeconds') ELSE '{}'::jsonb END
                WHERE monitor_type = 'HttpAvailability' AND deleted_at IS NULL;
                DROP FUNCTION web_health.http_policy_fingerprint_v2(text, text, boolean, integer, integer,
                    integer, integer, integer, integer, text, text, text, text, integer, integer);
                """);
    }
}

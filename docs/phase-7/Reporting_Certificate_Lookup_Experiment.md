# Reporting certificate lookup experiment

Date: 2026-09-09

Fixture: the preserved representative reporting database with 192 monitors, 96 SSL monitors, and 1,667,520 check results and snapshots.

The previous application plan grouped every current certificate observation, joined the result and snapshot tables twice, sorted 8,640 full certificate rows, and returned 96 rows. Its recorded execution duration was 617.674 ms.

An initial `DISTINCT ON` experiment reduced the sorted payload but still checked all 8,640 results. It took 4,516.045 ms with cold reads and was rejected.

The adopted query starts with each requested monitor and uses a lateral lookup ordered by `observed_at DESC, logical_check_id DESC`. PostgreSQL follows the existing `(endpoint_monitor_id, observed_at DESC)` observation index and stops after the newest observation whose result is current. It returns only the fields needed by the certificate summary and their recorded threshold snapshot.

```sql
SELECT monitor.id, latest.logical_check_id
FROM web_health.endpoint_monitor monitor
CROSS JOIN LATERAL (
    SELECT observation.logical_check_id
    FROM web_health.certificate_observation observation
    JOIN web_health.check_result result
        ON result.logical_check_id = observation.logical_check_id
        AND result.endpoint_monitor_id = observation.endpoint_monitor_id
    WHERE observation.endpoint_monitor_id = monitor.id
        AND result.current_state_disposition = 'Current'
    ORDER BY observation.observed_at DESC, observation.logical_check_id DESC
    LIMIT 1
) latest
WHERE monitor.monitor_type = 'SslCertificate'
    AND monitor.deleted_at IS NULL;
```

The representative plan returned all 96 rows in 67.367 ms. It performed 96 searches of the existing observation index and examined two candidate observations per monitor on average. No index, schema, retention policy, or current-state rule changed.

The complete database-foundation script passed after implementation. Its SSL scenario verifies that superseded scheduled observations cannot replace current evidence, while later current manual and urgent observations remain eligible. The release build completed with zero warnings and errors.

This single-query measurement identifies the local improvement. The three-second dashboard gate still requires the full ten-iteration application benchmark.

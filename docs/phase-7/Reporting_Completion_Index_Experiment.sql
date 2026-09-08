BEGIN;
EXPLAIN (ANALYZE, BUFFERS)
SELECT monitor.id,
    (SELECT max(check_row.completed_at)
     FROM web_health.logical_check check_row
     JOIN web_health.check_result result ON result.logical_check_id = check_row.id AND result.endpoint_monitor_id = check_row.endpoint_monitor_id
     WHERE check_row.endpoint_monitor_id = monitor.id AND check_row.source = 'Scheduled'
       AND check_row.state = 'Completed' AND result.current_state_disposition = 'Current')
FROM web_health.endpoint_monitor monitor WHERE monitor.deleted_at IS NULL;
CREATE INDEX retention_completion_experiment ON web_health.logical_check
    (endpoint_monitor_id, completed_at DESC NULLS LAST, id DESC)
    WHERE source = 'Scheduled' AND state = 'Completed';
EXPLAIN (ANALYZE, BUFFERS)
SELECT monitor.id,
    (SELECT check_row.completed_at
     FROM web_health.logical_check check_row
     JOIN web_health.check_result result ON result.logical_check_id = check_row.id AND result.endpoint_monitor_id = check_row.endpoint_monitor_id
     WHERE check_row.endpoint_monitor_id = monitor.id AND check_row.source = 'Scheduled'
       AND check_row.state = 'Completed' AND result.current_state_disposition = 'Current'
     ORDER BY check_row.completed_at DESC NULLS LAST, check_row.id DESC LIMIT 1)
FROM web_health.endpoint_monitor monitor WHERE monitor.deleted_at IS NULL;
ROLLBACK;

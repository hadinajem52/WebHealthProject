# Reporting result configuration identity experiment

## Decision

Persist the immutable configuration identity on each finalized `check_result` and read it directly when reporting evaluates comparability.

The identity is the existing canonical tuple of configuration fingerprint, snapshot schema version, and current-truth generation. Monitor source remains a separate comparability input. Historical rows are backfilled from their immutable configuration snapshots, and a database constraint rejects empty identities.

No reporting index was added.

## Representative fixture

Measurements used PostgreSQL 18 with 96 endpoints, 192 monitors, 1,667,520 check results, and 90 retained days. Each scenario ran ten measured iterations against the unchanged performance-baseline gates.

## Migration evidence

The first populated upgrade completed in 478.312 seconds including EF startup. Validation returned 1,667,520 results, 1,667,520 non-null identities, zero empty identities, and zero values that differed from the source snapshot tuple.

The populated `Down` migration retained all 1,667,520 results and removed the column. Reapplying the migration completed in 433.010 seconds and returned the same identity validation counts. Design-time EF commands use a 15-minute command timeout for this bounded one-time rewrite; normal application query limits are unchanged.

## Query evidence

The direct identity query removed the 1,667,520-row configuration-snapshot join. The default 30-day dashboard improved from a previous p95 of 3,929 ms to 2,837 ms and passed the three-second gate. Client-filtered and viewer-scoped 30-day dashboards passed at 773 ms and 712 ms p95. The 30-day CSV export completed at 884 ms p95.

The unfiltered 90-day dashboard improved to 8,239 ms p95 but remained over budget. Exact raw summary and trend aggregation became the remaining dominant work.

Three follow-up experiments were rejected:

- Combining summary and trend with grouping sets produced 7,507 ms p95 for 90 days and approximately 864 MB of temporary I/O in the captured plan.
- Raising benchmark-session `work_mem` to 128 MB produced 8,002 ms p95 for 90 days.
- A 285 MB covering index produced 6,609 ms p95 for 90 days, which did not justify its storage and write cost.

The rejected query and index changes were removed. The remaining performance work must preserve the exact raw-window percentile contract and the unchanged 90-day gate.

## Verification

- Release solution build: passed with zero warnings and zero errors.
- Populated upgrade, `Down`, and re-upgrade: passed with stable result counts.
- Full ordered database-foundation suite: passed all 22 stages in 1 minute 6 seconds after a clean Release build with zero warnings and zero errors.

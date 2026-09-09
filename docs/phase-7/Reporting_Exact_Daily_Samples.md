# Exact daily reporting samples

## Decision

Completed UTC days retain their exact responded-duration samples in `monitoring_daily_aggregate` while raw results exist. Reports use these compact arrays for exact p50 and p95 calculations and use bounded raw monitor-day ranges only where no completed aggregate exists. When raw retention starts for a day, it clears the exact samples atomically and reports use the retained histogram with the existing approximate-percentile disclosure.

The hourly retention coordinator prepares missing or legacy daily aggregates before any deletion category runs. Preparation uses the same transaction advisory lock, cancellation deadline, dry-run behavior, deterministic ordering, and configured batch limit as the deletion batches.

Scheduled counts now come from `logical_check.source`. Aggregate source bounds preserve the result transport source, including `WebHealthSafeHttpV1` and `WebHealthSslProbeV1`.

## Representative evidence

Fixture: PostgreSQL 18, .NET 10.0.12, 96 endpoints, 192 monitors, 1,667,520 results, 17,280 monitor-day aggregates, and 90 days of retained history. Ten measured iterations followed warm-up and plan capture.

| Scenario | P95 |
|---|---:|
| Dashboard, unfiltered, 30 days | 639 ms |
| Dashboard, one client, 30 days | 102 ms |
| Dashboard, unfiltered, 90 days | 1,310 ms |
| Dashboard, viewer scoped to one client, 30 days | 119 ms |
| CSV export, unfiltered, 30 days | 202 ms |
| Report dataset, unfiltered, 90 days | 1,415 ms |

All dashboard scenarios satisfy the three-second NFR-02 gate. No new reporting index was added.

## Verification

- Release solution build: zero warnings and zero errors.
- Unit suite: 832 passed.
- Clean-slate representative reporting performance suite: passed in 5 minutes 24 seconds including fixture creation and measurement.
- Ordered database-foundation suite: all 22 stages passed in 1 minute 10 seconds, including clean migration, upgrade, downgrade, repeatability, constraint, retention, reporting, cancellation, and permission checks.

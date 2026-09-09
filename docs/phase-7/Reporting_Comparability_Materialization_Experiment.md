# Reporting comparability materialization experiment

Date: 2026-09-09

Fixture: the preserved representative reporting database with 192 monitors and 1,667,520 check results and configuration snapshots.

## Trigger

The full ten-iteration application benchmark after the certificate lookup optimization still failed the unchanged three-second dashboard gate:

| Scenario | Fastest | Median | P95 | Verdict |
|---|---:|---:|---:|---|
| Dashboard, unfiltered, 30 days | 3,589 ms | 4,243 ms | 5,967 ms | over budget |
| Dashboard, one client, 30 days | 913 ms | 1,090 ms | 1,322 ms | within budget |
| Dashboard, unfiltered, 90 days | 10,054 ms | 10,902 ms | 16,200 ms | over budget |
| Dashboard, viewer scoped to one client, 30 days | 861 ms | 1,012 ms | 1,308 ms | within budget |
| CSV export, unfiltered, 30 days | 741 ms | 784 ms | 870 ms | informational |
| Report dataset, unfiltered, 90 days | 9,870 ms | 10,611 ms | 13,151 ms | informational |

The new certificate statement took 11.374 ms with `auto_explain`. The remaining dominant 30-day statement was comparability at 3,369.580 ms. PostgreSQL joined 547,293 eligible results to snapshots and externally sorted the wide identity tuples before returning 96 distinct identities. The parallel sorts used about 66 MiB of temporary disk in total.

## Adopted shape

The distinct identity query is now isolated in a materialized CTE. PostgreSQL can hash the five identity fields first and then sort the 96 grouped rows needed by the existing deterministic streaming hash.

```sql
WITH raw_identities AS MATERIALIZED (
    SELECT result.endpoint_monitor_id, snapshot.configuration_fingerprint,
        snapshot.schema_version, snapshot.current_truth_generation, result.monitor_source
    FROM raw_results result
    JOIN web_health.check_configuration_snapshot snapshot
        ON snapshot.logical_check_id = result.logical_check_id
    WHERE result.counts_for_uptime
    GROUP BY 1, 2, 3, 4, 5
)
SELECT * FROM raw_identities ORDER BY 1, 2, 3, 4, 5;
```

The prepared representative query returned the same 96 identities in 1,568.519 ms. The hash aggregate used 553 KiB and the final 96-row quicksort used 36 KiB. A direct warmed measurement of the same shape took 1,245.491 ms.

A covering snapshot-index experiment took 3,390.444 ms because 547,293 index probes caused more reads. It was rolled back. No index, schema, reporting contract, window, workload, or acceptance threshold changed.

The complete database-foundation script passed after implementation. It includes raw and aggregate history, configuration/generation drift, source comparability, CSV, all later ordered stages, and explicit migration application. The release build completed with zero warnings and errors.

The full dashboard benchmark still needs to be rerun after this change. These query measurements do not by themselves close NFR-02.

# Representative monitoring evidence

Generated from controlled `.test` targets. No third-party target was contacted.

| Property | Value |
|---|---|
| OS | Microsoft Windows 10.0.26200 |
| Process architecture | X64 |
| Logical processors | 12 |
| .NET runtime | .NET 10.0.12 |
| PostgreSQL | PostgreSQL 18.1 on x86_64-windows, compiled by msvc-19.44.35221, 64-bit |
| Execution model | One dispatcher, one reconciler, 100 concurrent request slots |
| Endpoints | 500 |
| HTTPS endpoints | 350 (70%) |
| HTTP monitors | 500 |
| SSL monitors | 350 |
| Configured global concurrency | 100 |

## Scheduling and restart recovery

| Measurement | Result |
|---|---:|
| Due monitors claimed | 500 |
| Initial queue acknowledgements | 499 |
| Dispatch duration | 9975 ms |
| Creation lag p95 | 84.0 s |
| Creation lag maximum | 89.0 s |
| Normal queue age p95 | 0.0 s |
| Normal queue age maximum | 0.0 s |
| Injected enqueue failures | 1 |
| Restart recovery claimed / acknowledged | 500 / 500 |
| Recovery duration | 1150 ms |
| Unique durable work IDs observed | 500 |
| Duplicate logical checks or durable work rows | 0 |

## Memory windows

Each window ran for 15 minutes after warm-up and repeatedly admitted 500 operations through the configured 100-slot global limiter.

| Window | Operations | Maximum concurrency | Start private bytes | End private bytes | Peak private bytes | Strictly monotonic |
|---|---:|---:|---:|---:|---:|---|
| 1 | 415500 | 100 | 66961408 | 57540608 | 66961408 | False |
| 2 | 416000 | 100 | 57729024 | 56418304 | 58335232 | False |

Neither window was strictly monotonic, and both ended below their starting private-memory value. This run showed no sustained monotonic growth. It is evidence for this machine and workload, not a universal memory guarantee.

## PostgreSQL outage recovery

The disposable PostgreSQL cluster was stopped after the load windows, connection refusal was confirmed, and the same data directory was restarted. Reconciliation then claimed and acknowledged 500 / 500 outstanding items in 3539 ms. The database still contained exactly 500 logical checks and 500 durable-work rows, and the queue observed 500 unique durable-work IDs. No database repair was performed.

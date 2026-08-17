# Hangfire scheduling and recovery

**Work item:** Phase 3 / WI-30 scheduling increment  
**Rules:** BR-S01, BR-S02, BR-S05, BR-S07, BR-S08  
**Acceptance criteria contribution:** AC-02 scheduled execution and restart recovery.

## Runtime and queue

The main web application uses Hangfire 1.8.24 with Hangfire.PostgreSql 1.21.1 and the application PostgreSQL connection. `HangfireSchedulingAndRecovery` installs the provider's version-23 schema through the explicit EF migration; runtime schema creation is disabled. The server activates only the `monitoring` short-check queue with at most four workers. Notification, crawler, and maintenance queues remain inactive until their owning phases.

Two lightweight recurring jobs run once per minute on the monitoring queue. One dispatches newly due monitors and one reconciles committed work whose enqueue or execution hand-off was interrupted. The recurring jobs do not create one Hangfire schedule per endpoint.

## Due-monitor dispatch

`MonitoringSchedulingService` selects a bounded UTC batch with `FOR UPDATE OF monitor SKIP LOCKED`. Eligibility requires an active client, enabled and non-deleted website, active and non-deleted environment, enabled and non-deleted endpoint and HTTP monitor, plus current target-authorization evidence for the endpoint host and port.

For each claimed monitor, one transaction creates:

- a queued scheduled logical check with the stable cadence key;
- its immutable effective configuration snapshot;
- one deduplicated `HttpCheck` durable-work row in `Dispatching` state;
- the monitor's next due time, advanced from its original schedule anchor to the first slot strictly after the dispatch instant.

The old due slot produces one catch-up check. Advancing directly to the first future anchored slot skips every other missed interval, so downtime does not create a request backlog. Retried execution never changes the monitor cadence.

New monitors are immediately due. HTTP defaults are five minutes for Production and fifteen minutes for non-Production environments. An Administrator can set a one-minute-to-24-hour endpoint override; the typed effective interval and override marker are snapshotted and audited. Environment type changes update inherited defaults without replacing endpoint overrides.

## Durable hand-off and recovery

After the application transaction commits, the dispatcher enqueues the existing logical-check and durable-work IDs. A successful enqueue changes only that work row from `Dispatching` to `Enqueued`. If enqueueing or acknowledgement is interrupted, the committed row remains recoverable.

Reconciliation uses another bounded `FOR UPDATE SKIP LOCKED` claim. It reclaims pending work, stale dispatch hand-offs, stale enqueued deliveries, and stale processing work whose execution lease is absent or expired. The row returns to `Dispatching`, is enqueued with the same IDs, and is acknowledged. Duplicate Hangfire delivery therefore converges on the existing logical check and cannot create another availability sample.

## Configuration

`Monitoring:Scheduling` controls the bounded batch sizes and recovery delay. Scheduling is enabled in the main application configuration with batches of 50 due monitors, 100 recovery rows, and a two-minute stale threshold. Tests and tooling that register infrastructure without starting the application leave scheduling disabled by default.

## Verification

The PostgreSQL 18 foundation gate proves:

- the Hangfire schema applies from a clean database and migration reapplication is a no-op;
- Production and non-Production defaults are 300 and 900 seconds;
- administrator endpoint overrides persist as the effective interval;
- one overdue monitor creates one logical check, snapshot, and durable-work row;
- `NextDueAt` moves to a future anchored slot and an immediate second dispatch creates no backlog;
- disabled client, website, environment, endpoint, or monitor, and expired target authorization, create no scheduled work;
- an interrupted enqueue remains `Dispatching` and restart reconciliation enqueues the same logical-check ID without creating another check;
- two competing dispatchers claim a due monitor once.

Manual checks and the monitoring history UI remain separate Phase 3 increments. Phase 4 still owns health projection, incidents, maintenance, and notifications.

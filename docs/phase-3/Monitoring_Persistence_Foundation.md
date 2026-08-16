# Monitoring persistence foundation

## Scope and rules

This increment establishes the durable execution boundary for BR-S01 through BR-S08 and the persistence prerequisites for AC-02. It does not enqueue Hangfire work, make outbound requests, create HTTP results/findings, or change endpoint health.

There is no user-visible action yet. Authorization for manual checks remains part of the later manual-check vertical slice.

## Data behavior

`MonitoringExecutionFoundation` adds:

- `logical_check` with Scheduled, Manual, and Urgent source-field constraints, UTC lifecycle timestamps, stable scheduled cadence identity, and current state;
- one immutable `check_configuration_snapshot` per logical check with typed effective values, allow-listed provenance, and a canonical fingerprint;
- numbered `execution_attempt` rows with bounded job, worker, outcome, and normalized failure-category fields;
- one `execution_lease` row per endpoint monitor with owner token, expiry, and monotonically increasing fencing generation;
- `durable_work` with a durable dedupe key, queue/state, availability time, bounded failure category, and complete-or-absent lease fields.

Existing endpoint monitors are backfilled from `created_at`; new monitors receive an immediate UTC schedule. Schedule anchor and due time are required after the migration. Interval changes preserve the anchor and select the first future anchored slot, preventing a backlog of every missed interval.

The migration adds due-monitor, logical-check history/state, lease-expiry, attempt, and durable-work indexes. Database constraints remain the final defense for source fields, positive attempt/generation values, timestamp ordering, lease completeness, cadence uniqueness, and durable-work idempotency.

Urgent checks require a request timestamp but allow a null initiating user so system-triggered checks do not need a fake identity. Manual checks continue to require an initiating user. Non-null warning and critical timing thresholds must be nonnegative in both mutable monitor configuration and immutable snapshots.

## Security and privacy

Configuration snapshots cannot be updated or deleted through normal database operations. Persisted configuration and failure fields are bounded. This increment stores no response bodies, headers, credentials, request objects, or arbitrary diagnostic objects.

The lease service uses an atomic PostgreSQL upsert. A composite foreign key guarantees that the leased monitor owns the referenced logical check. An unexpired lease cannot be replaced; an expired or explicitly released lease advances the fencing generation so a stale worker cannot later be treated as the current owner.

A deferred PostgreSQL constraint trigger requires the immutable configuration snapshot before a logical check becomes Queued, Running, or Completed. This permits the check and snapshot to be inserted in either order within one transaction without allowing execution to use mutable current configuration.

## Verification

- `MonitorCadenceTests` cover UTC initialization, exact interval boundaries, missed-slot catch-up, future anchors, invalid intervals, and offset-independent cadence keys.
- The isolated PostgreSQL 18 gate covers clean migration application, Phase 2 upgrade/backfill, repeated application as a no-op, scheduled cadence uniqueness, snapshot requiredness and immutability, monitor/check lease consistency, competing lease claims, release, expiry recovery, system Urgent/manual source rules, nonnegative thresholds, and fencing advancement.
- `dotnet ef migrations has-pending-model-changes` reports no drift.
- Release builds complete with zero warnings and errors.

## Compatibility and deferred work

Apply `MonitoringExecutionFoundation` explicitly before Phase 3 services begin creating logical checks. Existing Phase 2 databases are supported and retain their monitor identity and configuration.

Hangfire configuration, scheduler claiming with `FOR UPDATE SKIP LOCKED`, logical-check creation orchestration, retries, reconciliation, HTTP results, findings, and history pages remain later Phase 3 increments.

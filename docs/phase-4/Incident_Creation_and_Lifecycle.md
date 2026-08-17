# Incident Creation and Lifecycle

## Work item

- Scope: Phase 4.4 incident creation, evidence, and lifecycle behavior.
- Rules: BR-I01–BR-I10; supports AC-03, AC-04, AC-12, and the incident portion of AC-13.
- User-visible behavior: confirmed failures open deduplicated critical incidents; authorized operators can acknowledge, begin work, resolve, close, reassign, and annotate them.
- Authorization: Administrator and Operations can manage all incidents. Developer/Support can manage incidents assigned to that user or one of their teams. Viewer cannot mutate incidents. Forced closure and reopening require Administrator.

## Automatic behavior

- Scheduled failures are evaluated in the same fenced PostgreSQL transaction that finalizes the logical check and health state.
- An incident opens only when the immutable check snapshot's consecutive-failure threshold is reached.
- The filtered unique index on `(endpoint_monitor_id, issue_key)` is the final defense against duplicate active incidents. Different stable issue keys can remain active simultaneously.
- Ownership is snapshotted from the endpoint override when present; otherwise the website owner is used.
- Opening, confirmed failure, recovery start, interrupted recovery, and confirmed recovery each append bounded evidence and timeline records.
- The first recovery pass enters `MonitoringRecovery`; a later failure resets it to `Open` or `InProgress` according to prior acknowledgement. The confirming recovery pass resolves the incident.
- Automatic resolution records the evidence logical-check ID and calculates recovery and outage durations from persisted timestamps.
- Manual, cancelled, ineligible, and maintenance-reset checks cannot create incidents or advance incident recovery.

## Manual lifecycle

The application decision engine permits these controlled transitions:

- `Open` → `Acknowledged`
- `Acknowledged` → `InProgress`
- any non-closed active state → `Resolved` with a category and note
- `MonitoringRecovery` → `Resolved` after confirmed recovery
- `Resolved` → `Closed`
- Administrator forced closure from a non-closed state with a bounded reason
- Administrator reopening from `Closed` with a bounded reason

Manual resolution requires a category of at most 50 characters and a note of at most 2,000 characters. Forced closure and reopening require an Administrator reason of at most 500 characters. Normal closure is accepted only from `Resolved`. Closed incidents otherwise reject mutations.

Every successful acknowledgement, progress change, resolution, closure, forced closure, reopening, reassignment, and note append is protected by the original version token and writes both an ordered incident timeline event and a typed, allow-listed audit event.

## Recurrence and database impact

- A new incident links to the most recent matching closed incident only when its close timestamp falls within the inclusive 30-day boundary before the new opening timestamp.
- The `IncidentLifecycle` migration adds recovery timestamps and durations, user-sourced resolution evidence, evidence-source constraints, and the expanded immutable timeline vocabulary.
- PostgreSQL retains the active-incident unique index, incident optimistic-concurrency token, monitor/check evidence foreign keys, nonnegative-duration checks, lifecycle timestamp ordering, and immutable evidence/timeline triggers.
- The migration is verified from a clean database, through downgrade/upgrade paths, under repeated application, and with no pending model changes.

## Security and privacy

- Automated evidence stores only the schema version, normalized outcome, stable failure category, and measurement timestamp. It excludes response bodies, headers, query values, credentials, and exception messages.
- Incident audit writes use a typed allow-list. Resolution note content is represented only by a presence flag in audit snapshots; the bounded operational note remains in the incident timeline/evidence record.
- Authorization is enforced in the application service and does not rely on a future UI.

## Verification

- `IncidentLifecycleEngineTests` covers valid and invalid transitions, manual field bounds, Administrator controls, fail-during-recovery behavior, the exact 30-day recurrence boundary, and duration calculations.
- The PostgreSQL foundation test verifies threshold opening, deduplication, owner precedence, recurrence, failure/recovery/resolution evidence, two-pass recovery, manual role and assignment rules, stale-version rejection, timeline sequencing, typed audit events, and migration invariants.
- The full delivery workflow runs the unit, integration, migration, audit, and Testcontainers gates.

# Health Confirmation and Recovery Engine

## Work item

- Scope: Phase 4.3 health confirmation and recovery.
- Rules: BR-I01, BR-I02, BR-I05, BR-I06, BR-M04; supports AC-03 and AC-04.
- User-visible behavior: current health changes only after scheduled evidence reaches the snapshotted confirmation threshold.
- Authorization: this is worker-owned processing; it adds no interactive endpoint or authorization surface.

## Behavior

- Scheduled terminal results update health in the same fenced transaction as check history, attempt completion, and durable-work completion.
- A monitor row is locked before its issue states are selected `FOR UPDATE`, serializing health decisions for that monitor and preventing competing issue-state creation.
- Each distinct stable issue key has independent consecutive-failure and consecutive-recovery counters.
- A pass resets pending failures. Therefore fail, pass, fail remains one failure and does not confirm an outage.
- The snapshot's failure threshold confirms `Critical`. The confirming logical-check ID and timestamp become the endpoint-health evidence.
- An initially unknown monitor becomes `Healthy` on its first qualifying pass.
- The first qualifying pass after `Critical` emits `RecoveryStarted` while confirmed health remains `Critical`. The snapshot's recovery threshold confirms `Healthy`; the final recovery check becomes the new evidence.
- Manual, cancelled, and target-ineligible checks do not change counters or confirmed health.
- Maintenance resets pending counters by default. A window with `ContinueFailureCounter` explicitly enabled continues normal confirmation behavior.
- Endpoint health is written only on a confirmed status transition, so later duplicate or same-status evidence does not replace the original confirming check.

`RecoveryStarted` is an explicit application decision consumed by the Phase 4.4 [incident lifecycle orchestration](Incident_Creation_and_Lifecycle.md) for monitoring-recovery and resolution changes.

## Data and migration impact

No migration is required. The implementation uses the Phase 4.1 `issue_state` and `endpoint_health` tables and their existing uniqueness, counter, concurrency, and evidence constraints.

## Security, privacy, and operations

Only stable issue keys, counters, statuses, timestamps, and logical-check identifiers are used. Response bodies, headers, query values, and exception text are not copied into health state. No new log payload or secret is introduced.

## Verification

- `HealthConfirmationEngineTests` covers two-failure confirmation, fail-pass-fail reset, two-pass recovery, initial health, source exclusions, and maintenance reset/continue policy.
- The PostgreSQL foundation test finalizes controlled 500/200 sequences through the real lease and transaction path, checks counter and status boundaries, and verifies that the evidence logical-check ID changes only at confirmation.
- The clean Testcontainers migration/foundation test passes with the health orchestration active.

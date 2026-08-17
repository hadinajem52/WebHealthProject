# Logical-check execution and idempotency

**Work item:** Phase 3 / WI-31 execution increment  
**Rules:** BR-S03–BR-S08, BR-H01, BR-H10  
**Acceptance criteria contribution:** AC-02 exactly-once logical history and AC-05 bounded terminal execution.

## Execution boundary

`ILogicalCheckExecutionService` owns one delivery of an existing logical check. It loads the immutable snapshot, rejects checks that are not queued/running, and returns immediately when the logical check is already completed. The service rechecks current endpoint eligibility before any outbound request and builds the transport request exclusively from the snapshotted timeout, redirect limit, body limit, current normalized endpoint identity, and production state.

Eligible and ineligible deliveries both require the current PostgreSQL execution lease before an attempt is recorded. The lease duration covers the snapshotted request timeout plus a bounded finalization buffer. A competing delivery that cannot acquire the lease makes no request and creates no attempt. Non-final delivery reports retry-required; the final Hangfire delivery reports reconciliation-required so a live owner is never overwritten or misreported as an HTTP result.

Each acquired delivery creates one numbered `execution_attempt` and moves the logical check from Queued to Running. The command identifies the exact `HttpCheck` durable-work row; unrelated work attached to the same logical check is not mutated. A later lease owner closes abandoned running attempts as `Superseded`. Structured logging scope carries `LogicalCheckId`, `DurableWorkId`, `EndpointId`, and `JobId`; exception messages and target content are not logged.

## Terminal results and retries

Real HTTP observations are finalized through `ILogicalCheckFinalizationService`. Target ineligibility and retry exhaustion use separate execution-terminal evidence and never fabricate transport results. Result insertion, findings, redirect hops, required attempt completion, logical-check completion, the targeted durable-work completion, and conditional consumption of the lease token and fencing generation occur in one transaction.

Current ineligibility produces a terminal `TargetIneligible` cancelled result without contacting the target and does not count as an uptime sample. Expected transport failures, including timeout, remain typed HTTP observations. Unexpected exceptions propagate so programming and dependency defects are not disguised as monitoring failures. If worker cancellation occurs after an attempt starts, a short independent cleanup token performs a fenced retry transition before cancellation is rethrown.

Retry transitions use the same conditional lease token and fencing generation as terminal finalization. A stale worker may close only its own attempt as `Superseded`; it cannot re-enqueue the winning worker's durable work or replace its result. Duplicate finalization closes a stale caller attempt without creating another availability sample.

The forward-only `LogicalCheckExecutionLifecycle` migration adds the `Superseded` attempt outcome and `TargetIneligible` result category. It upgrades databases that already contain the monitoring foundation and also applies cleanly from an empty database.

`LogicalCheckJob` is a thin Hangfire adapter on the isolated `monitoring` queue. It passes the logical-check and durable-work identities plus Hangfire job/server identity and final-attempt state to the orchestration service. Retry-required and reconciliation-required dispositions are surfaced as explicit job failures. Stale enqueued/processing work and expired leases are reclaimed by [Hangfire scheduling and recovery](Hangfire_Scheduling_and_Recovery.md).

## Verification

The isolated PostgreSQL 18 gate proves:

- snapshot timeout propagation into the safe transport request;
- current eligibility rejection before transport execution;
- one completed result and one attempt for successful execution;
- duplicate delivery of a completed check is a no-op;
- an unexpected transport exception propagates, and a later lease owner supersedes the abandoned attempt while producing one result;
- HTTP timeout becomes a terminal transport result;
- ineligible work is terminal, makes no request, and is excluded from uptime;
- worker cancellation uses a fenced retry transition and does not persist a false cancelled sample;
- a competing live lease creates no attempt, makes no request, and requests retry/reconciliation;
- stale retry/finalization cannot mutate the winning durable work or result;
- unrelated durable-work rows remain unchanged;
- migration reapplication remains a no-op.

## Deferred

Manual-check authorization and history pages remain separate later Phase 3 increments. No Phase 4 health, incident, maintenance, or notification behavior is introduced here.

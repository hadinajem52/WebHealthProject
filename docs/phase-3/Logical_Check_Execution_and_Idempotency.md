# Logical-check execution and idempotency

**Work item:** Phase 3 / WI-31 execution increment  
**Rules:** BR-S03–BR-S08, BR-H01, BR-H10  
**Acceptance criteria contribution:** AC-02 exactly-once logical history and AC-05 bounded terminal execution.

## Execution boundary

`ILogicalCheckExecutionService` owns one delivery of an existing logical check. It loads the immutable snapshot, rejects checks that are not queued/running, and returns immediately when the logical check is already completed. The service rechecks current endpoint eligibility before any outbound request and builds the transport request exclusively from the snapshotted timeout, redirect limit, body limit, current normalized endpoint identity, and production state.

Eligible and ineligible deliveries both require the current PostgreSQL execution lease before an attempt is recorded. The lease duration covers the snapshotted request timeout plus a bounded finalization buffer. A competing delivery that cannot acquire the lease makes no request and creates no attempt.

Each acquired delivery creates one numbered `execution_attempt` and moves the logical check from Queued to Running. Structured logging scope carries `LogicalCheckId`, `EndpointId`, and `JobId`; exception messages and target content are not logged.

## Terminal results and retries

Safe transport outcomes, including timeout and cancellation, are normalized and finalized through `IHttpCheckHistoryService`. Result insertion, findings, redirect hops, attempt completion, logical-check completion, durable-work completion, and verification/consumption of the lease token and fencing generation occur in the finalization transaction.

Current ineligibility produces a terminal cancelled result without contacting the target and does not count as an uptime sample. Unexpected infrastructure failure records a bounded retryable attempt and releases the lease. A later Hangfire delivery uses the same logical-check ID; the result primary key prevents a second availability sample. When the configured Hangfire deliveries are exhausted, the final delivery records a terminal `ExecutionExhausted` result and a `TerminalFailure` attempt instead of leaving the logical check open.

`LogicalCheckJob` is a thin Hangfire adapter on the isolated `monitoring` queue. It passes Hangfire job/server identity and final-attempt state to the orchestration service. Only the explicit `RetryRequired` outcome is converted into an exception for Hangfire's bounded retry filter.

## Verification

The isolated PostgreSQL 18 gate proves:

- snapshot timeout propagation into the safe transport request;
- current eligibility rejection before transport execution;
- one completed result and one attempt for successful execution;
- duplicate delivery of a completed check is a no-op;
- retryable infrastructure failure followed by success creates two attempts but one logical result;
- exhausted work and HTTP timeout become terminal results;
- ineligible work is terminal, makes no request, and is excluded from uptime;
- a competing live lease creates no attempt and makes no request;
- finalization still rejects stale lease tokens/fencing generations;
- migration reapplication remains a no-op.

## Deferred

The next increment owns due-monitor claiming, Hangfire PostgreSQL server registration, enqueue/reconciliation, cadence advancement, catch-up behavior, and restart recovery. Manual-check authorization and history pages remain separate later Phase 3 increments. No Phase 4 health, incident, maintenance, or notification behavior is introduced here.

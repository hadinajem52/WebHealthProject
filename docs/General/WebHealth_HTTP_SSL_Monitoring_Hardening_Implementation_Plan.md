# Phase 7 — HTTP and SSL Monitoring Hardening Implementation Plan

**Status:** Implementation-ready  
**Project profile:** Personal internship/portfolio project, implemented and operated by one owner.  
**Goal:** Make HTTP and SSL monitoring deterministic, race-safe, conservative against false positives, and maintainable without replacing the existing monitoring architecture.

This plan does **not** claim production certification, independent audit, HA, or enterprise deployment readiness. Completion means representative evidence that the monitoring feature behaves correctly within the defined scope.

This is the HTTP/SSL monitoring-hardening subplan for Phase 7. It does not replace the remaining Phase 7 work in the phased and detailed implementation plans, including retention, holds, aggregates, and any separately accepted deployment work.

---

## 1. Architecture contract

Preserve the existing pipeline:

```text
EndpointMonitor
    -> LogicalCheck
    -> immutable configuration snapshot
    -> DurableWork
    -> Hangfire
    -> ExecutionLease
    -> HTTP transport / SSL probe
    -> finalization
    -> result / health / incident / notification / reporting
```

The hardening work changes how evidence is represented and when it is allowed to mutate current state.

### 1.1 Authoritative state model

Do **not** use one overall `Healthy / TargetFailure / Inconclusive` verdict as confirmation evidence.

Each completed observation produces issue-level evidence values:

```text
IssueEvidenceStatus
    Passed
    Failed
    Inconclusive
    NotApplicable
```

Conceptual model:

```text
IssueEvidence
    IssueKey
    Status
    Severity?              # only when meaningful
    FailureCategory?       # bounded canonical vocabulary
```

Examples:

```text
Http.Availability       Passed
Http.Latency            Failed
Http.Content            Inconclusive

Ssl.Expiry              Passed
Ssl.HostnameMismatch    Failed
Ssl.Untrusted           Inconclusive
Ssl.NotYetValid         Passed
```

The overall result outcome is a derived display/reporting summary only. Confirmation and recovery operate **per issue**.

`Passed` means an applicable issue was evaluated conclusively and is not currently present. `NotApplicable` is reserved for rules that genuinely do not apply to the monitor configuration, such as HTTP content validation when no required marker is configured. Do not use `NotApplicable` merely because a failure condition is absent.

Names such as `Http.Availability` and `Ssl.Expiry` in this plan identify issue families, not replacement persisted issue keys. Existing canonical issue-key builders remain authoritative, including their version, failure-category, scope, and certificate-fingerprint components required by BR-I03, BR-I04, BR-C05, and BR-C06.

`IssueEvidence[]` is a transient classifier result, not a new persistence table in this iteration. Durable history and state continue to use the existing immutable raw result/observation, findings, `IssueState`, `EndpointHealth`, incident evidence/events, and notification records. If future reporting requires every pass, inconclusive, or not-applicable classification to be stored independently, that requires a separately recorded schema and retention decision.

### 1.2 Current-state eligibility is separate from observation meaning

Every finalized check also receives one current-truth disposition:

```text
Applied
Superseded
OutOfOrder
Ineligible
HistoricalOnly
```

Deterministic precedence:

```text
unsupported snapshot for current truth
    -> HistoricalOnly

monitor identity / generation / fingerprint mismatch
    -> Superseded

state sequence <= last applied sequence for the generation
    -> OutOfOrder

current authorization or lifecycle eligibility fails
    -> Ineligible

source mode = Historical
    -> HistoricalOnly

source mode = Full or FenceOnly
    -> Applied
```

Persist one primary disposition plus optional bounded secondary diagnostic reasons. Secondary reasons never change the primary state-transition decision.

### 1.3 Maintenance is not a disposition

Maintenance remains an independent policy decision:

```text
MaintenanceDecision
    IsActive
    CounterMode = Ignore | Reset | Count
    SuppressNotifications
    PauseEscalation
    CountsForUptime
    PrimaryOccurrenceId?
    OccurrenceIds
```

Do not reduce maintenance to a single `Suppressed` flag.

### 1.4 Source participation and confirmation rules

Use three source modes:

```text
Scheduled  -> Full       # sequence-fencing + automated confirmation/recovery
Urgent SSL -> FenceOnly  # sequence-fencing, but confirmation/recovery uses Ignore
Manual     -> Historical # no sequence and no automated current-state mutation
```

`FenceOnly` exists so a newer urgent observation can prevent older same-generation work from rewriting current truth without artificially satisfying confirmation thresholds.

Only `Applied` evidence with `CounterMode.Count` may advance automated confirmation/recovery:

```text
Failed + CounterMode.Count
    -> advance failure confirmation

Passed + CounterMode.Count
    -> advance recovery confirmation

Inconclusive / NotApplicable
    -> advance neither failure nor recovery

CounterMode.Reset
    -> reset the affected pending streak according to existing maintenance semantics

CounterMode.Ignore
    -> leave the affected streak unchanged
```

A different issue remaining failed must not block recovery of an issue that has conclusively passed.

---

# 2. Phase 1 — Lock behavior with failing tests

Implement tests before schema or engine changes.

## 2.1 Per-issue evidence tests

Required:

```text
availability passes while latency fails
content inconclusive while availability passes
SSL expiry recovers while hostname mismatch remains
SSL hostname recovers while chain trust remains unknown
one SSL observation produces multiple simultaneous failures
one issue inconclusive does not erase another issue's conclusive pass/fail
```

## 2.2 Ordering and lifecycle tests

Required:

```text
same generation: newer StateSequence applies before older -> older is OutOfOrder
new generation completes before old generation -> old is Superseded
newer inconclusive observation fences an older conclusive observation from becoming current
pause -> resume cannot resurrect pre-pause work
archive -> restore cannot resurrect pre-archive work
disable -> enable cannot resurrect pre-disable work
target identity change cannot allow new target evidence to recover old-target incident
```

## 2.3 Maintenance regression tests

Preserve existing policy behavior:

```text
SuppressionPolicy=None -> notifications remain deliverable
SuppressAll -> notifications suppressed
ContinueFailureCounter=true -> counters continue
ContinueFailureCounter=false -> counters reset
PauseEscalation=true -> escalation clock pauses
PauseEscalation=false -> escalation continues
maintenance samples remain excluded from uptime when required
```

## 2.4 Source-eligibility tests

Current baseline:

```text
Scheduled -> automated confirmation eligible
Manual -> historical/display evidence only; does not advance automated confirmation
Urgent SSL -> does not independently advance confirmation until BR-C07 explicitly says otherwise
```

If urgent SSL is later made confirmation-eligible, add anti-double-counting rules so a scheduled result and its immediate urgent recheck cannot satisfy a multi-observation threshold accidentally.

**Exit gate:** tests demonstrate the existing incorrect/undefined cases before implementation changes.

---

# 3. Phase 2 — Schema and immutable snapshot v2

Upgrade the current snapshot-v1 architecture to snapshot v2. Preserve completed v1 history.

## 3.1 `EndpointMonitor`

Add/retain:

```text
current_truth_generation bigint not null default 1
state_sequence_counter bigint not null default 0
last_applied_state_sequence bigint null
```

Rules:

```text
current_truth_generation >= 1
state_sequence_counter >= 0
last_applied_state_sequence >= 1 when non-null
```

`state_sequence_counter` is monotonic for the lifetime of the monitor. It does not need to reset when generation changes; generation is checked first. Clear `last_applied_state_sequence` when generation advances so the new generation starts with no accepted current observation.

Generation increments atomically in the same transaction when queued work can become stale, including:

```text
target URL / host / effective port change
normalization-version change
production classification change
effective monitor policy / fingerprint change
monitor or parent lifecycle enable/disable
scheduling mode or pause/resume
archive / restore / retirement / replacement
reconciliation that replaces monitor identity/policy
```

Do not increment for ordinary scheduling bookkeeping such as `NextDueAt`.

## 3.2 `LogicalCheck`

Persist state-ordering fields directly on the row:

```text
current_truth_generation bigint not null for state-eligible automated work
state_sequence bigint null
```

Rules:

```text
Scheduled -> state_sequence required; SourceMode=Full
Urgent SSL -> state_sequence required; SourceMode=FenceOnly
Manual -> state_sequence null; SourceMode=Historical
```

The snapshot also contains the captured generation, but the logical-check columns exist so concurrency and database constraints do not depend on joining snapshot JSON/rows.

## 3.3 Overlap prevention

Enforce at most one non-terminal scheduled check per monitor and generation.

Retain the existing scheduled cadence-key uniqueness constraint. Cadence-key uniqueness preserves logical scheduling idempotency across retries and completed history; the new partial index independently prevents overlapping non-terminal scheduled work.

Use the repository's actual terminal-state invariant. Prefer a filtered/partial unique index over a reliable non-terminal predicate such as `completed_at IS NULL` if that is the existing contract.

Conceptually:

```sql
UNIQUE (endpoint_monitor_id, current_truth_generation)
WHERE source = 'Scheduled' AND completed_at IS NULL
```

Do not use `state <> 'Completed'` unless the state model truly has only one terminal state.

The scheduler must treat unique-conflict loss as "another dispatcher already created the check", not as an operational failure.

## 3.4 State sequence allocation

`StateSequence` is monotonic for each monitor and therefore monotonic within every generation.

Allocate it transactionally by atomically incrementing `EndpointMonitor.state_sequence_counter` in the same transaction that creates state-fencing work, then copy the resulting value to `LogicalCheck.state_sequence`. Do not use wall-clock time as ordering authority. Gaps are harmless. A value belonging to committed work must never be reused; a transaction rollback may restore an uncommitted increment because no observable work owns that value.

Required property:

```text
same MonitorId
    older state-fencing work has a lower sequence than newer work
```

## 3.5 Snapshot v2

Snapshot v2 freezes:

```text
resolved normalized target
host
port
normalization version
production classification
monitor type
complete effective HTTP/SSL policy
configuration fingerprint
current truth generation
```

Current authorization is never frozen. Re-authorize immediately before each outbound connection, including every redirect hop.

Compatibility:

```text
schema_version = 1 -> historical compatibility only after v2 cutover
schema_version = 2 -> current-truth eligible when all other gates pass
```

Completed v1 checks are not rewritten.

**Exit gate:** migrations apply from empty DB, upgrade an existing DB, preserve v1 history, and database tests prove generation/sequence/overlap constraints.

---

# 4. Phase 3 — Evidence classifier and confirmation engine

## 4.1 Keep classification pure

Implement a pure domain classifier unless external dependencies are genuinely required.

Suggested shape:

```text
ClassifyHttpObservation(result, policy) -> IssueEvidence[]
ClassifySslObservation(observation, policy, observedAt) -> IssueEvidence[]
```

Do not create an interface/service pair solely for abstraction.

## 4.2 Extend the existing `HealthConfirmationEngine`

Do not introduce a parallel confirmation subsystem.

Extend the existing engine to consume per-issue evidence:

```text
EvaluateIssue(
    currentIssueState,
    IssueEvidence,
    CounterMode,
    failureConfirmationCount,
    recoveryConfirmationCount)
```

The engine remains deterministic and side-effect free. Persistence/orchestration remains outside it.

## 4.3 Confirmation cadence

Do not add a configurable time-based confirmation window in this iteration. Preserve the existing consecutive-logical-check semantics defined by BR-I01, BR-I02, and BR-I05.

Generation changes, conclusive passes, maintenance reset behavior, and source/disposition eligibility prevent stale or ineligible samples from contributing to a current streak. If a demonstrated sparse-sample defect later requires an age bound, record the business-rule change first and define its exact formula, timestamps, schema, migration compatibility, configuration surface, and boundary tests before implementation.

## 4.4 Inconclusive semantics

Classify monitoring-local inability to reach a conclusion as `Inconclusive`, not target failure.

Examples:

```text
unsupported/malformed content encoding for body validation
body truncated before required content marker can be evaluated
local parser failure
local storage/worker failure
chain trust could not be determined -> Ssl.Untrusted = Inconclusive
```

A conclusive transport target failure remains `Failed` for the relevant issue.

**Exit gate:** pure unit tests cover every evidence state, multi-issue recovery, streak/reset/ignore behavior, and confirmation-count boundaries.

---

# 5. Phase 4 — Finalization ordering and current-truth fencing

Implement one finalization path for scheduled HTTP/SSL state mutation.

## 5.1 Finalization order

Within the finalization transaction:

```text
1. lock the logical-check row and current EndpointMonitor row with FOR UPDATE
2. persist immutable raw result/observation
3. classify IssueEvidence[]
4. resolve current monitor identity + generation + fingerprint from the locked state
5. resolve current authorization/lifecycle eligibility
6. determine the preliminary disposition from snapshot, identity, generation, authorization, lifecycle, and source mode
7. if the preliminary disposition is Applied, compare StateSequence against last_applied_state_sequence while the monitor lock is held and finalize Applied or OutOfOrder
8. resolve MaintenanceDecision at the observation instant
9. derive effective CounterMode from source mode + maintenance (`FenceOnly` always Ignore)
10. evaluate per-issue confirmation/recovery using existing HealthConfirmationEngine
11. persist issue state / EndpointHealth changes only when the engine produces a transition
12. apply incident transitions
13. create notification events through existing notification path
14. update last_applied_state_sequence for accepted Full or FenceOnly observations
15. commit
```

Do not allow a partially failed transaction to update sequence state without its corresponding health/incident mutation.

Preserve the current logical-check and monitor row-locking design. The sequence comparison and update must not rely only on an earlier unlocked read or application-level optimistic comparison.

## 5.2 Sequence rule

For the current generation:

```text
if state_sequence <= last_applied_state_sequence
    -> OutOfOrder
    -> persist history only
```

Important: a newer **inconclusive** `Full` or `FenceOnly` observation still advances `last_applied_state_sequence`. This prevents an older conclusive result from arriving later and rewriting current truth.

The inconclusive observation itself does not advance issue failure/recovery streaks. A `FenceOnly` urgent SSL result also advances the sequence fence while leaving confirmation/recovery untouched.

## 5.3 Historical results

`Superseded`, `OutOfOrder`, `Ineligible`, and `HistoricalOnly` results remain queryable for history/diagnostics but must not:

```text
mutate EndpointHealth
advance/reset issue streaks
open/escalate/recover/resolve incidents
emit state-transition notifications
create urgent follow-up work
```

**Exit gate:** contention/integration tests prove completion-order independence and no stale-current-state mutation.

---

# 6. Phase 5 — HTTP monitoring correctness

## 6.1 Availability and status

Produce independent evidence at minimum for:

```text
Http.Availability
Http.Status
Http.Latency
Http.Content
```

Do not collapse these into one success/failure flag.

## 6.2 Redirect behavior

Configured accepted 3xx responses **never bypass redirect security**.

Every redirect hop still applies:

```text
redirect count limit
loop detection
Location validation
current target authorization
network destination policy / SSRF restrictions
scheme policy
HTTPS/TLS validation
```

A configured accepted 3xx may affect the status-policy evidence, but it does not authorize an unsafe redirect destination.

## 6.3 Body validation

Use bounded body reads.

Required semantics:

```text
marker found inside complete/accepted body -> Passed
marker conclusively absent from complete body -> Failed
body truncated before absence can be proven -> Inconclusive
unsupported/malformed decoding prevents trustworthy evaluation -> Inconclusive
```

Do not report `ContentMissing` merely because the byte cap was reached.

## 6.4 Latency

Latency failure must not convert successful availability into availability failure.

Evaluate warning and critical latency thresholds against `CheckResult.TotalDurationMs`, as required by BR-P02. Preserve `CheckResult.TtfbDurationMs` as a separately stored and reported metric under BR-P01; TTFB does not replace total duration as the threshold input.

## 6.5 Monitoring-local failures

Separate target failures from monitoring infrastructure/local failures in the canonical failure vocabulary.

Examples of local/infrastructure categories:

```text
WorkerFailure
PersistenceFailure
ParserFailure
UnsupportedEncoding
LocalResourceLimit
```

These do not become target incidents unless an explicit product rule says so.

## 6.6 Timeout migration

Preserve existing monitors currently configured/effectively operating at 30 seconds during migration.

Use the specification's 15-second default only for new monitors or explicit reset/update paths that adopt the new default.

Do not silently rewrite existing monitoring behavior.

**Exit gate:** HTTP integration tests cover status, latency, redirects, authorization, body truncation, malformed encoding, timeout compatibility, local failures, and simultaneous issue evidence.

---

# 7. Phase 6 — SSL monitoring correctness

## 7.1 Independent SSL issues

Evaluate independently:

```text
Ssl.Expiry
Ssl.HostnameMismatch
Ssl.Untrusted
Ssl.NotYetValid
```

A single certificate may produce several simultaneous findings.

The singular display category remains compatibility/UI only and never suppresses other issue evidence.

## 7.2 Exact-instant validity

Use the observation instant for validity:

```text
observedAt < NotBefore -> NotYetValid Failed
observedAt >= NotAfter -> Expiry Failed as expired
otherwise -> leaf currently valid
```

Do not use rounded calendar-day logic to decide whether the certificate is already expired.

Days-remaining values may be displayed, but they are not the authoritative expiry boundary.

## 7.3 Chain trust

Model:

```text
Trusted
Untrusted
Unknown
```

Mapping:

```text
Trusted   -> Ssl.Untrusted Passed
Untrusted -> Ssl.Untrusted Failed
Unknown   -> Ssl.Untrusted Inconclusive
```

Never convert `Unknown` into `Untrusted` merely to avoid an unknown state.

## 7.4 Certificate-controlled networking

The SSL probe and normal HTTPS transport must perform zero certificate-directed CRL/OCSP/AIA downloads unless a separately approved design explicitly adds them.

Invalid certificates must never be accepted by ordinary application HTTP traffic simply so they can be inspected.

Do not persist DER, private keys, raw chains, certificate-controlled URLs, or revocation payloads.

## 7.5 Urgent SSL checks

Keep urgent SSL checks useful for fresh evidence/history and make them `FenceOnly`: they receive a `StateSequence` and may advance the sequence fence, but their effective `CounterMode` is always `Ignore` in this iteration.

Therefore an urgent check can stop an older same-generation result from becoming current without independently advancing failure or recovery confirmation. Revisit full confirmation eligibility only after BR-C07 is explicitly updated with anti-double-counting semantics.

**Exit gate:** tests cover exact validity boundaries, `Unknown` vs `Untrusted`, simultaneous faults, renewal while another fault remains, urgent checks, and zero certificate-controlled networking.

---

# 8. Phase 7 — Maintenance and incident continuity

## 8.1 Preserve existing maintenance policy

Use the existing centralized maintenance resolver/decision path.

Do not replace it with unconditional suppression.

Required independent outputs:

```text
CounterMode
SuppressNotifications
PauseEscalation
CountsForUptime
```

HTTP and SSL must consume the same maintenance semantics as other monitoring types.

## 8.2 Generation-transition incident rules

Document and implement these rules explicitly.

### Same target, policy-only change

```text
retain confirmed issue state and active incident
reset pending confirmation streaks unless existing policy explicitly preserves them
require fresh evidence under the new generation before the next transition
```

### Pause/resume or parent disable/enable

```text
retain confirmed health and incident identity
apply documented maintenance/lifecycle counter semantics
pre-transition work is fenced by generation
```

### Target identity change

Examples:

```text
URL/host/effective port changes to a different monitored target
monitor replacement/retirement
```

Rules:

```text
1. atomically advance CurrentTruthGeneration
2. reset pending confirmation/recovery streaks for the new target identity
3. archive any active old-target incident through the existing incident archive path
4. record an audit reason such as TargetIdentityChanged; do not emit a recovery event
5. preserve the archived incident and all historical evidence
6. new target evidence begins under the new generation and cannot recover the archived old-target incident
```

Archiving here means "this incident no longer represents the configured target", not "the target recovered". Do not fabricate recovery for a target that was never observed healthy.

### Archive/restore

```text
retain historical incidents/evidence
pre-archive work cannot mutate restored current state
restored monitor requires fresh post-restore evidence for future transitions
```

## 8.3 Manual evidence

Preserve current behavior:

```text
manual checks do not advance automated confirmation/recovery
```

Before allowing manual results to attach to active incidents as non-state-changing evidence, verify and explicitly align the formal specification. Do not change this implicitly in the hardening implementation.

**Exit gate:** integration tests cover every generation transition with active incidents and prove no cross-target false recovery.

---

# 9. Phase 8 — Freshness and monitoring-system health

Keep **execution freshness** separate from **conclusive-evidence freshness**.

## 9.1 Execution freshness

Track when scheduled work last completed, regardless of whether the observation was conclusive.

Only completed scheduled checks refresh scheduled execution freshness. Manual and urgent work do not.

## 9.2 Conclusive evidence freshness

Track the latest time current-state-eligible evidence produced at least one conclusive issue result relevant to the monitor.

An inconclusive result may prove the scheduler/worker is alive while still leaving monitoring truth stale.

Expose both dimensions so operators can distinguish:

```text
target currently failing
monitoring recently executed but could not conclude
monitoring pipeline itself stale/broken
```

## 9.3 Monitoring infrastructure failures

Keep monitoring-system health separate from monitored-target health.

At minimum diagnose:

```text
scheduler heartbeat
worker availability / queue coverage
oldest queued work
oldest overdue monitor
reconciliation heartbeat
repeated local finalization failures
```

A monitored website being down must not make application readiness fail.

Use low-cardinality metrics only. Do not use endpoint/URL/host/monitor IDs as metric dimensions.

**Exit gate:** diagnostics can answer whether the target is unhealthy, evidence is inconclusive/stale, or WebHealth monitoring itself is unhealthy.

---

# 10. Phase 9 — Notification idempotency verification

Treat notification idempotency as **verification/regression work first**, not a new subsystem.

Inspect the existing notification event/delivery uniqueness constraints and writer behavior.

Required invariant:

```text
one incident transition/event occurrence
    -> at most one logical notification event
    -> at most one delivery per intended destination/channel key
```

Add duplicate-finalization and retry tests.

Only add new schema/logic if the existing path cannot prove this invariant.

**Exit gate:** duplicate jobs, duplicate finalization attempts, and worker retries do not produce duplicate user-visible notifications.

---

# 11. Phase 10 — Migration and rollout

## 11.1 Migration order

Implement in this sequence:

```text
1. failing regression tests
2. schema: generation + sequence + logical-check overlap fields/indexes
3. snapshot v2 + v1 compatibility
4. per-issue classifier
5. HealthConfirmationEngine extension
6. finalization disposition/sequence fencing
7. HTTP semantics
8. SSL semantics
9. maintenance + incident continuity
10. freshness/diagnostics
11. notification idempotency verification
12. representative failure/race suite
```

Do not combine all schema, engine, HTTP, SSL, and incident changes into one unreviewable commit.

## 11.2 Existing data

Migration must:

```text
preserve completed v1 snapshots
initialize CurrentTruthGeneration = 1 for existing monitors unless already present
leave historical completed checks immutable
avoid fabricating StateSequence for old completed history unless required solely for compatibility
create all new scheduled work as v2 after cutover
preserve existing 30-second timeout behavior
```

After v2 cutover, v1 results may remain historical but may not mutate current monitoring truth.

## 11.3 Rollback safety

Every migration `Down` path must be valid for data allowed by `Up`, or explicitly document a forward-only migration before implementation if safe rollback is impossible.

Do not silently drop evidence/state that the old schema cannot represent.

---

# 12. Phase 11 — Representative false-positive and race evidence

This is a project evidence gate, **not certification**.

## 12.1 Zero-tolerance correctness cases

Required count = 0 for:

```text
duplicate terminal result
competing finalization mutating current state twice
older same-generation result overwriting newer state
pre-generation result mutating new generation
new target recovering old-target incident
inconclusive observation counted as failure
inconclusive observation counted as recovery
one issue blocking another issue's valid recovery
overlapping scheduled checks for same monitor/generation
manual/urgent check unexpectedly advancing automated confirmation
accepted 3xx bypassing redirect authorization/security
truncated body producing false ContentMissing
Unknown SSL trust becoming Untrusted
certificate-controlled outbound request
unauthorized target connection
duplicate incident transition notification
```

## 12.2 Failure injection

Exercise:

```text
worker stop before request
worker stop after request before finalization
worker stop during finalization
DB failure during finalization
enqueue failure
lease expiry/reclaim
duplicate Hangfire delivery
scheduler restart
DNS failure
HTTP timeout
TLS failure
body truncation / malformed encoding
concurrent lifecycle mutation while check runs
archive/restore while work is queued
URL change while old work is running
```

Supported recovery cases require no manual database repair.

## 12.3 Representative scale

Use controlled local/CI targets only. Keep the existing representative portfolio-scale target (about 500 endpoints) and configured concurrency bounds.

Record:

```text
machine/runtime/database versions
worker counts
fixture shape
scheduler creation lag
queue age
completion/finalization latency
memory behavior
failure injection results
```

These measurements are evidence for this project environment, not universal SLAs.

---

# 13. Definition of done

The hardening work is complete only when all of the following are true:

- Per-issue `Passed / Failed / Inconclusive / NotApplicable` evidence drives confirmation and recovery.
- Overall result status is not used as the sole issue-state oracle.
- Snapshot v2 freezes execution target/policy/generation while current authorization remains authoritative.
- `CurrentTruthGeneration` fences lifecycle/policy races.
- `StateSequence` deterministically orders same-generation state-eligible observations.
- The database prevents overlapping scheduled checks for the same monitor/generation.
- A newer inconclusive observation prevents an older result from becoming current without itself creating false failure/recovery evidence.
- Maintenance keeps independent counter, notification, escalation, and uptime semantics.
- Existing `HealthConfirmationEngine` is reused and extended rather than duplicated.
- Manual checks remain non-confirming; urgent SSL remains non-confirming until explicitly redesigned.
- Target identity changes cannot strand or falsely recover incidents.
- HTTP status, availability, latency, and content are independent issues where appropriate.
- Truncation or malformed decoding cannot create false content-missing incidents.
- Accepted 3xx responses cannot bypass redirect security.
- Existing 30-second monitor behavior is preserved during migration; 15 seconds is the new/reset default.
- SSL expiry uses exact observation instants.
- SSL `Unknown` trust is not treated as `Untrusted`.
- Certificate-controlled networking remains zero.
- Execution freshness and conclusive-evidence freshness are reported separately.
- Monitoring infrastructure health is distinct from target health and application readiness.
- Existing notification idempotency is verified or minimally fixed where proven insufficient.
- The zero-tolerance race/false-positive suite passes.
- Migrations, compiled model/schema assertions, unit tests, integration tests, and representative evidence all pass.
- Documentation describes the result as representative monitoring-hardening evidence for a personal internship/portfolio project, not production or independent certification.

---

# 14. Work-item traceability

Each implementation slice inherits the relevant row below. No row introduces a new application role or changes current server-side authorization unless a recorded specification change explicitly says so.

| Work item | Business rules / criteria | Behavior, authorization, inputs, outputs, and errors | Data and migration impact | Security, privacy, and operational signals | Tests, documentation, and compatibility |
|---|---|---|---|---|---|
| Per-issue classifier and confirmation engine | BR-I01–BR-I06, BR-U01–BR-U03; AC-03, AC-04, AC-15 | Convert bounded normalized HTTP/SSL observations into transient `IssueEvidence[]`; reject unknown statuses, severities, or blank canonical keys; preserve existing role behavior. | Reuse `IssueState`, `EndpointHealth`, findings, and incidents; no evidence table or new configurable confirmation window. | Do not persist bodies, secrets, or unsafe diagnostics; classification failures are monitoring-local signals, not target failures. | Pure unit tests cover simultaneous issues, independent recovery, inconclusive and not-applicable behavior, and confirmation boundaries; update monitoring behavior documentation. |
| Generation, snapshot v2, sequence, and overlap fencing | BR-S01, BR-S03–BR-S05, BR-I03, BR-I04; AC-02, AC-12, AC-15 | Scheduled and urgent creation captures immutable target/policy data; manual behavior and authorization remain unchanged; unique-conflict loss is a benign duplicate-dispatch result. | Add generation/sequence columns, snapshot-v2 fields, checks, indexes, compiled-model assertions, and explicit migration compatibility while retaining cadence-key uniqueness and v1 history. | Re-authorize before every connection and redirect; record bounded disposition/audit reasons; expose repeated stale/duplicate contention without high-cardinality metric labels. | Empty/upgrade/repeatability database tests, row-lock contention tests, lifecycle-race tests, and migration documentation; existing completed v1 checks remain historical. |
| HTTP correctness | BR-H01–BR-H10, BR-P01–BR-P04, BR-S04; AC-02, AC-05, AC-10, AC-15 | Consume snapshot target/policy and return raw result plus independent availability, status, latency, and content evidence; validation/configuration failures are bounded and monitoring-local; no authorization-policy change. | Reuse `CheckResult`, `RedirectHop`, and `Finding`; migrate only bounded vocabulary or snapshot fields required by the implementation. | Apply SSRF/destination authorization and TLS validation on every hop; use bounded reads and safe diagnostics; expose timeout, parser, encoding, and local-resource failures separately. | Controlled HTTP fixtures cover status, redirects, truncation, encoding, timings, timeout compatibility, and direct-request authorization; document `TotalDurationMs` threshold semantics and preserved 30-second monitors. |
| SSL correctness and urgent fencing | BR-C01–BR-C07, BR-I03–BR-I06; AC-06, AC-12, AC-15 | Consume the requested host and snapshot policy and return certificate observation plus independent issue evidence; urgent checks are `FenceOnly`; canonical fingerprint-aware keys remain authoritative. | Reuse certificate observations, findings, issue state, and incidents; extend bounded trust/category vocabularies only where required. | Perform zero certificate-controlled networking, never weaken ordinary HTTPS validation, and never persist DER, chains, private keys, or certificate-controlled URLs; expose `Unknown` trust separately. | Controlled TLS fixtures cover exact validity boundaries, trust states, hostname mismatch, renewal, simultaneous findings, urgent ordering, and no outbound revocation/AIA traffic; update SSL behavior documentation. |
| Maintenance and incident continuity | BR-M01–BR-M04, BR-I03–BR-I10, BR-N01, BR-N06, BR-U02; AC-03, AC-04, AC-09, AC-13, AC-15 | Reuse current maintenance and incident authorization; independently apply counter, notification, escalation, and uptime decisions; invalid lifecycle transitions remain rejected and audited. | Preserve existing maintenance occurrences and incident history; add only generation-transition audit vocabulary or constraints proven necessary. | Retain marked operational evidence without leaking unsafe result data; emit bounded audit and escalation-pause signals. | Integration tests cover each generation transition with active incidents, maintenance policy combinations, cross-target recovery prevention, audit history, and local/demo compatibility. |
| Freshness and monitoring-system diagnostics | BR-N07, BR-R01; AC-10, AC-15 | Authorized diagnostics users can distinguish target failure, recent inconclusive execution, and stale/broken monitoring; target failure never fails application readiness. | Reuse existing check timestamps and diagnostic projections first; add indexed fields only when the required query cannot be supported correctly and efficiently. | Keep metric dimensions low-cardinality and diagnostics free of target URLs, hosts, identifiers, and exception details. | Tests cover scheduled-only execution freshness, conclusive freshness, stale queues/workers, and readiness separation; document diagnostic meanings and local thresholds. |
| Notification idempotency and representative evidence | BR-N01–BR-N03, BR-N06–BR-N08, NFR-01; AC-03, AC-04, AC-12, AC-15 | Preserve existing recipient/channel authorization and delivery behavior; duplicate finalization or worker retry produces at most one logical event and delivery per destination/channel key. | Verify existing event/delivery uniqueness first; add the smallest constraint or writer correction only if a regression test proves a gap. | Templates use approved bounded fields; notification failures remain retryable diagnostics and never roll back check/incident truth. | Duplicate-delivery tests, failure injection, and controlled portfolio-scale evidence run in local/CI environments; record environment and limitations without certification claims. |

---

# 15. Recommended commit slices

```text
1. test(monitoring): add per-issue and race regression matrix
2. db(monitoring): add generation, state sequence and overlap constraints
3. feat(monitoring): introduce snapshot v2 compatibility
4. refactor(monitoring): add per-issue evidence classifier
5. refactor(health): extend HealthConfirmationEngine for issue evidence
6. fix(monitoring): add disposition and state-sequence finalization fencing
7. fix(http): harden status, latency, redirects and body evidence
8. fix(ssl): harden independent SSL evidence and exact validity
9. fix(maintenance): preserve counter/escalation/notification policy semantics
10. fix(incidents): define generation and target-identity continuity
11. feat(monitoring): split execution and conclusive-evidence freshness
12. test(notifications): verify idempotency under duplicate finalization
13. test(monitoring): run representative false-positive/failure evidence suite
14. docs(monitoring): update Phase 7 evidence and limitations
```

This sequence is the working implementation order. Do not start later phases by inventing alternative semantics that contradict sections 1–4 of this document.

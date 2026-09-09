# Monitoring hardening evidence

## P7-MON-01 — immediate correctness

Implements BR-Q01, BR-Q02, BR-Q04, BR-Q07 and monitoring lifecycle/type safeguards;
preserves AC-02, AC-06, AC-10 and AC-15 behavior.

The shared connector validates the raw DNS answer count before normalization and deduplication,
rejects the whole destination if any answer is prohibited, and attempts permitted addresses in
resolver order. Each attempt owns a fresh socket and a per-IP concurrency slot. The address
wait/connect timeout defaults to five seconds, bounded to one through ten seconds. Caller and
whole-request cancellation remain absolute deadlines. A failed address advances to the next;
peer-policy rejection fails closed. Host and global leases continue to cover the whole request.
Only identifiers and safe failure categories enter monitoring logs; no new target or content
logging was introduced.

`EndpointMonitorReconciler` owns HTTP/SSL creation and reconciliation. Endpoint archive retires
only active rows with the same actor and timestamp as the endpoint, without changing cadence,
due time, scheduling mode, or pause choice. Restore considers only that archive operation,
ordered by creation descending and ID ascending, and keeps PageAudit retired. Registry edits
cannot change an archived target, so matching archive retirement also preserves target identity.
SSL host/port changes retire the active SSL identity and create its replacement through the
DbSet. Restoring an endpoint leaves the endpoint disabled. Current-truth generation is added
in P7-MON-02 with the snapshot schema change.

HTTP policy updates now switch explicitly by monitor type. SSL retains its dedicated update;
PageAudit stays in its own configuration path; unknown types throw within the transaction.
Execution likewise selects HTTP or SSL explicitly and rejects other types before transport.
Existing server-side role and assignment checks remain in place.

No schema migration or compiled-model update is needed for this increment. The new timeout
setting and bounds are documented in `setup/README.md`.

## Verification

Recorded 2026-09-08 on the local Windows workspace, .NET 10.0.400 SDK / 10.0.11 runtime:

| Command | Result |
| --- | --- |
| `dotnet test tests/WebHealth.UnitTests --verbosity quiet` | 738 passed |
| `dotnet test tests/WebHealth.IntegrationTests --filter 'FullyQualifiedName!~DatabaseFoundationTests' --verbosity quiet` | 621 passed, 3 existing skips |
| `scripts/run-database-foundation-tests.ps1` | Full ordered suite passed, explicit migration database updated; Release build had zero warnings/errors |
| `git diff --check` | Passed |

The database regression owns a registered endpoint and verifies historical retirement metadata,
manual-only and paused state, cadence, active monitor identity, PageAudit non-restoration, and
HTTP policy isolation from SSL/PageAudit. Existing database stages exercise SSL replacement
on host/port/scheme changes. Transport coverage includes IPv6 failure followed by mapped IPv4
success, raw-answer limits before deduplication, mixed prohibited DNS answers, per-address
queue timeout fallback, caller cancellation, slot reacquisition, redirects, TLS validation, and
whole-request deadlines. Direct authorization tests pass in the ordinary integration suite.

The broader run exposed a pre-existing badge-test parser defect: its regex failed on label spans
with `data-live` attributes. The test now parses the DOM and checks each badge's label text;
no badge rendering was changed.

This is local personal-project evidence, not deployment certification. Later increments remain
required for snapshot immutability, authorization revocation, SSL hardening, diagnostics,
retention, and representative load/recovery evidence.

## P7-MON-02 — implemented

Snapshot v2 freezes target URL, host, port, normalization, production classification, policy,
and monitor generation. Scheduled, manual, and urgent creation share the builder. HTTP/SSL
execution and evidence validation use the recorded target; v1 compatibility is explicit.
PostgreSQL triggers advance generations atomically for policy and lifecycle mutations, including
set-based parent cascades. Cadence updates alone do not advance generation. Finalization locks
and refreshes current state; superseded/ineligible history cannot change health, issue counters,
incidents, notifications, or urgent work.

Current target authorization is checked immediately before every socket, including HTTP redirects
and DNS fallback. Administrator/Operations can grant or revoke endpoint/host/port evidence through
endpoint Actions. Existing endpoints receive no fabricated permission. Evidence/reasons are absent
from snapshots, logs, and audit payloads; grant/revoke audits record identifiers and action only.

Verification on 2026-09-08:

| Check | Result |
| --- | --- |
| Unit suite | 738 passed |
| Ordinary integration suite | 634 passed, three existing skips |
| Full ordered database foundation script | Passed, including explicit migration application |
| EF pending-model check | No model drift |
| Browser against disposable PostgreSQL fixtures | Endpoint superseded message visible; permission empty state, grant to Active, revoke to Revoked verified |
| Visual inspection | Dashboard fonts/cards retained; corrected an empty validation-summary box found during inspection |

Database regressions cover pause/resume, scheduling toggle, endpoint disable/enable, archive/restore,
policy changes, all parent lifecycle toggles, HTTP scheme changes, SSL host/port/scheme replacement,
and recorded-target execution after edits. Old results persist without current health/issues/incidents.
Transport tests prove revoked permission causes zero socket contacts and fallback rechecks permission.
Direct service and MVC tests cover roles, anti-forgery, duplicate grants, idempotent revoke, audit privacy,
identity matching, and expiry boundaries. Snapshot tests cover legacy resolution, missing/conflicting
v2 fields, unsupported versions, and database rejection of invalid snapshots.

An isolated populated database regression found and now protects a rollback defect. Rollback cancels
unfinished v2 work, clears leases, preserves completed result facts, and converts snapshots to the
legacy representation before dropping target fields. It flushes deferred constraints before schema
changes. Reapplication is repeatable. This explicit rollback loses v2-only target/disposition facts
and permission grants; the setup guide documents that limitation. Normal upgrades preserve v1 history.

The migrations have only been applied to disposable verification databases, not the user's application
database. The missing Detail_Page_UI_Pattern.md reference was checked; existing endpoint cards and
fact rows supplied the UI reference. Mobile visual checks and broader changed-UI evidence remain part
of the final release gate. Increments 3–7, including AC-14/AC-15, remain required.

## P7-MON-03 — implemented

Typed v2 HTTP overrides share one policy resolver across registry updates and queued snapshots.
New and reset monitors use a 15-second timeout. The data migration preserves existing materialized
values, including 30-second timeouts, as explicit overrides. Status codes are bounded and canonical;
thresholds, confirmation counts, marker length, and comparison modes are validated before saving.
Configuration drift prevents snapshot creation and reports identifiers without marker text.
Scheduled and manual checks capture equivalent resolved policy, with each setting's source.

Endpoint forms expose overrides and clearing them restores defaults. Detail rows show effective
values and sources. Audit facts expose marker presence only. Marker matching decodes the bounded
buffer as UTF-8, US-ASCII, or ISO-8859-1, falling back to UTF-8 for unsupported or malformed charsets.
No response content is reread or persisted.

Verification on 2026-09-08:

| Check | Result |
| --- | --- |
| Unit suite | 767 passed |
| Ordinary integration suite | 634 passed, three existing skips |
| Additional legacy JSON compatibility tests | 3 passed |
| Full ordered database foundation script | Passed; Release build had no warnings or errors |
| EF pending-model check | No model drift; migration changes data only |
| Browser validation | Rejected status 500 without saving; accepted and canonicalized 404, 301, 404 |
| Browser edit and reset | Saved 30-second timeout, three failures, and literal marker; reloaded values; clearing restored 15 seconds, two failures, and no marker/status overrides |
| Visual inspection | Desktop detail cards and 390-pixel mobile layout retained dashboard styling; marker HTML displayed as text |

Database evidence includes named policy fixtures, atomic invalid-update rejection, drift rejection
without queued work, safe audit payloads, scheduled/manual snapshot equivalence, marker/status
execution, and a 15-second reset. An isolated migration database proves 30-second preservation,
repeatable upgrade, populated rollback, and retention of historical snapshot marker facts.
Existing transport and authorization suites protect redirect security, 2xx/5xx behavior, bounded
bodies, role restrictions, and anti-forgery. Setup documents rollback loss of current v2-only fields.

Browser verification used the disposable foundation database with workers and email disabled.
One deliberately orphaned endpoint from a negative database fixture was archived only in that
preview database so the registry editor could list valid fixtures. The browser tool's empty-fill
operation did not clear controls; keyboard selection and Backspace verified the actual reset flow.
No user application database migration was applied. Increments 4–7 remain required.

## P7-MON-04 — implemented

The first slice adds a shared offline TLS policy to the normal HTTP handler and inspection probe:
certificate downloads are disabled, revocation is not checked, and certificate verification flags
remain strict. Normal HTTPS still has no certificate-validation override. The probe rejects its
handshake after inspection and sends no application data.

A certificate with local AIA issuer, OCSP, and CRL URLs and a deliberately unavailable issuer is
served by an OpenSSL loopback fixture. Both client paths are checked against a separate listening
socket for zero certificate-controlled connections. The fixture uses OpenSSL because the Windows
TLS test server made its own OCSP requests even with managed offline settings. This test also
exposed chain-wide PartialChain errors being missed by the probe's per-element trust evaluation;
chain-wide errors now participate in trust evaluation. Resolved SSL policy, structured persistence,
simultaneous findings, and the remaining increment gates are still pending.

Verification on 2026-09-08: 59 transport, SSL probe, and chain-trust integration tests passed.

The next slice evaluates leaf time validity, hostname matching, chain trust, and expiry bands
independently. Several findings can be persisted for one certificate. The display category follows
NotYetValid, Expired, HostnameMismatch, Untrusted, ExpiringSoon precedence. Rule keys use
`Ssl.NotYetValid`, `Ssl.HostnameMismatch`, and `Ssl.Untrusted`; their issue identities retain the
existing category-based encoding so existing incident identity and historical references remain
stable. Expiry keeps its fingerprint-specific issue key. Threshold validation now requires strict
warning > high > critical >= 0 ordering. The unit suite passed 771 tests on 2026-09-08.
The full ordered database foundation script passed, including persistence of three simultaneous SSL findings and issue states. The Release build completed with zero warnings and errors.

An increment-3 follow-up found that the database still rejected equal HTTP warning/critical
thresholds despite the resolver accepting this documented boundary. `HttpThresholdEquality`
relaxes both monitor and snapshot checks. The existing policy workflow now saves equal thresholds
and queues matching snapshots; the populated upgrade/rollback fixture also uses equal thresholds.
Rollback retains the relaxed checks to preserve those policies and immutable historical values.
Verification: full ordered database suite and explicit migrations passed on 2026-09-08; Release build had zero warnings/errors; EF reported no pending model changes. Compiled-model regeneration produced no structural changes.

The SSL probe now captures canonical chain-status names from both element and chain-wide flags: NoError is removed, flags are expanded, names are deduplicated and ordinal-sorted, and output is bounded to 32 names. The observation carries these facts without certificate-controlled strings or encoded certificate bytes. Persistence and UI wiring remain pending. Verification: 60 transport, probe, and chain-trust integration tests passed, including PartialChain evidence from the local incomplete-chain handshake.

Structured certificate persistence is now implemented with separate leaf validity, hostname,
chain trust, and JSON chain-status fields. The endpoint reader and detail view expose these facts
and the revocation limitation. Historical positive trust is conservatively backfilled as Unknown;
negative trust and recorded hostname matches remain available, and validity is derived from the
observation time. The migration does not invent historical status codes. Setup documents explicit
application and rollback loss. Browser verification, stronger schema/backfill checks, and SSL
policy snapshots remain pending before the increment can be marked complete.
Verification on 2026-09-08: full ordered database suite and explicit migrations passed; the new simultaneous-fault fixture verifies all four persisted fields. Release build passed without warnings/errors, compiled model was regenerated, and EF reported no model drift.

ResolvedSslPolicy now defines the daily cadence, 15-second timeout, one-check confirmations, and strictly ordered 30/15/7 expiry defaults. New SSL monitor construction consumes its effective timing/counts. Its SSL-specific canonical fingerprint includes all policy fields, URL, and production classification; four focused tests passed and the infrastructure build passed without warnings/errors. Fingerprint migration and snapshot threshold wiring remain pending, so existing fingerprint storage is unchanged in this slice.

SSL expiry thresholds are now copied into v2 snapshots and consumed by finalization. The database
requires all three values for SSL v2 and enforces strict ordering; HTTP snapshots leave them null.
The migration backfills earlier v2 SSL snapshots with their previously effective 30/15/7 defaults.
V1 compatibility is explicit. SSL-specific fingerprint adoption and remaining evidence gates are
still pending.
Verification on 2026-09-08: full ordered database suite passed, including a 20-day certificate remaining healthy under its snapshotted 10-day warning threshold. Explicit migrations, zero-warning Release build, compiled-model regeneration, and EF no-model-drift check passed.

SSL-specific fingerprints are now used for new monitors and registry updates. Finalization accepts
a canonical hash or an exact computed legacy hash, with legacy compatibility limited to the original
expiry defaults. It requires agreement between logical check and snapshot hashes. The data migration
converts only known hashes for active SSL monitors, advances generation, and preserves queued and
completed historical checks. Its reverse operation recognizes matching default-expiry canonical
hashes rather than rewriting unknown policies. Five focused policy tests passed.
Verification on 2026-09-08: 776 unit tests passed; ordinary Release integration suite passed 640 tests with four opt-in skips (database foundation, Docker, reporting baseline, and live SMTP). The database foundation script passed separately, including populated fingerprint rollback/upgrade, repeatability, and completion of preserved legacy queued work as Superseded. Release build had zero warnings/errors and EF reported no model drift.

A display regression found that the endpoint certificate card used default expiry thresholds and
could select superseded observations. The reader now limits its current certificate to results
accepted as Current and calculates expiry severity from that observation's immutable snapshot.
Expiry severity remains independent of hostname/trust faults. The database regression exercises
a current renewal with a non-default warning threshold followed by a later superseded expired
observation, ensuring the card retains the renewal and its recorded policy.
Verification on 2026-09-08: the full ordered database suite passed with the reader regression; Release build had zero warnings/errors and explicit migrations completed successfully.

The SSL snapshot interval source now identifies the policy profile. An isolated populated upgrade
check verifies conservative certificate backfill (historical positive trust becomes Unknown,
without invented chain codes) and preserved 30/15/7 expiry defaults.

Browser inspection found the dashboard certificate query still selected superseded evidence.
It now uses Current results and the observation's recorded expiry thresholds, matching endpoint
detail. The endpoint expiry badge also remains visible alongside hostname or trust failures.
The database regression checks that a later superseded expired observation cannot put a current,
healthy certificate back into the dashboard attention list.
Verification on 2026-09-08: the full ordered database suite passed; the updated Razor page built
in Release with zero warnings and errors. Final responsive browser verification remains pending.

Certificate attention totals now subtract the overlap between invalid certificates and expiry bands, so simultaneous faults do not count one certificate twice. The focused regression passed on 2026-09-08; the test build compiled the application and web projects successfully.

The populated SSL scenario now presents a genuinely different SHA-256 fingerprint during renewal.
It verifies that the old expiry incident is Resolved with CertificateRenewed classification and
exactly one CertificateRenewed event. Schema inspection asserts all four structured certificate
columns and all three snapshot expiry columns. BR-C01 through BR-C07 traceability now links this
hardening evidence, and BR-C02 explicitly documents structured facts and storage/network limits.
Verification on 2026-09-08: the full ordered database suite passed with zero-warning Release build.

Responsive browser verification on 2026-09-08 used the disposable database with scheduling and
notifications disabled. Desktop and 390-by-844 mobile inspection confirmed current certificate
validity, hostname, chain, recorded expiry severity, revocation limitation and wrapped fingerprint.
The later superseded expired check remains labeled historical evidence. The dashboard reports the
current renewal healthy. Mobile content did not overflow horizontally (375px content at 390px viewport).
The temporary viewport was reset and the preview application/database were stopped afterward.
A read-only query found zero endpoint_health rows with confirmed_status = Disabled; no legacy
health backfill is justified for increment 5 in this fixture.

### P7-MON-05 endpoint operational-state slice

Endpoint detail now exposes confirmed health and derived operation separately for active HTTP/SSL
monitors. Precedence is Disabled, ManualOnly, Paused, Delayed, NeverChecked, Stale, Active. Lifecycle
eligibility is evaluated independently of whether any schedule is enabled. The projection retains
underlying base state, delay, stale cutoff and last scheduled completion. Legacy Disabled health
maps to Unknown without rewriting persisted data. DispatchDelayGrace defaults to ten minutes and
is bounded to two through thirty minutes at startup.

Verification on 2026-09-08: 11 unit cases passed for precedence, exact grace/freshness boundaries,
daily cadence and legacy compatibility. The full ordered database suite passed with real persisted
manual, urgent and superseded checks excluded from scheduled freshness, and all-paused/manual-only
monitors correctly classified. Ordinary integration: 640 passed, four opt-in skips. Release build
had zero warnings/errors. This is a partial increment: new status-row browser evidence, dashboard/
CSV wiring, runtime heartbeats, protected diagnostics and telemetry remain pending.

### P7-MON-05 reporting separation

Dashboard rows and exports now carry the same MonitorOperationalState projection as endpoint detail.
ConfirmedStatus, health filters and health counts preserve confirmed health when schedules stop;
legacy persisted Disabled maps to Unknown. Disabled is no longer a health filter. Dashboard rows
show each monitor's operational state independently, and CSV appends OperationalState and
LastScheduledCompletionAt. The summary's DisabledMonitorCount is a lifecycle-disabled count,
separate from health totals. Existing StatusBeforeDisabled DTO compatibility values are null.

Verification on 2026-09-08: 23 report-query unit cases, 57 status projection/filter cases, and all
648 ordinary integration tests passed (four opt-in skips). The full ordered database suite passed,
including stopped-monitor export health, operation and freshness, authorized filter combinations,
and parsed CSV/screen equality for health and operation. Release build had zero warnings/errors.
Updated dashboard/status-row browser verification remains pending with the diagnostics UI work.

### P7-MON-05 scheduler runtime persistence

Dispatch and reconciliation now record invocation start, latest success/failure, duration, bounded
failure category and consecutive failures in monitoring_runtime_state. Zero-work runs count as
successful invocations; partial enqueue reports QueueEnqueue. Recovery resets the counter and
retains last failure time. Completion uses a separate context with a five-second deadline, and an
invocation token prevents an older overlapping completion from overwriting the newer record.
Migration 20260908135926_MonitoringRuntimeState creates no fake heartbeat rows. Compiled model,
expected migrations/tables/entities and exact column assertions are updated. No target or exception
text is persisted. Database outages can prevent recording; completion-write failures emit safe logs.

Verification on 2026-09-08: full ordered database suite passed with zero-work, three consecutive
failures, enqueue failure, cancellation, recovery, late overlapping completion and actual scheduler
entry-point assertions. An isolated populated rollback/upgrade and repeatability check passed.
Release build had zero warnings/errors; EF reported no pending model changes. Protected runtime
presentation, worker heartbeat adapter, monitoring-health thresholds and metrics remain pending.

### P7-MON-05 engine health and protected diagnostics

MonitoringEngineHealth evaluates scheduler success age (3/5 minutes), consecutive failures,
worker availability/short-check queue coverage/two-minute heartbeat, queue age (5/15 minutes),
and dispatch overdue grace/30-minute critical age. Disabled scheduling reports Healthy with
DisabledByConfiguration. Hangfire DTOs remain inside the worker adapter and no worker IDs enter
application output. Reporting scopes monitor/work facts using existing selection and authorization;
Viewer output excludes the detailed runtime object. Administrators and Operations can open the
protected diagnostics page and /health/monitoring. Readiness remains independent of target health.

Verification on 2026-09-08: 18 threshold cases and 49 authorization baseline cases passed, including
all four roles against both new routes. Ordinary integration: 656 passed, four opt-in skips. Full
ordered database suite passed with real Hangfire server announcement/queue/heartbeat evidence,
Viewer runtime isolation and disabled-scheduling health. Latest Release build: zero warnings/errors.
Desktop and 390-by-844 mobile browser inspection verified the diagnostics page, preserved runtime
history, disabled-mode wording and no horizontal page overflow. Inspection caught missing mobile
history labels; the corrected stacked rows were rebuilt and visually verified. Dashboard and
endpoint detail showed confirmed Unknown separately from operational Disabled. Fixture timestamps
include deliberately advanced test clocks. The browser blocked the JSON health URL; direct HTTP
role tests cover that route. Temporary viewport, preview process and PostgreSQL were cleaned up.
Execution scopes, metrics and final increment-5 review remain pending.

### P7-MON-05 bounded telemetry and execution context

Transport attempts and scheduler operations now publish operation counts and duration through
WebHealth.Monitoring. Only monitor_type, source, operation and failure_category are dimensions;
all values pass bounded allowlists. Execution scopes include the required identifiers, attempt,
source, snapshot schema and generation. Transport faults log a safe category without raw exception
text. These operation metrics do not represent confirmed target health. Export remains optional.

Verification on 2026-09-08: the real MeterListener regression rejected arbitrary URLs, identifiers
and certificate-like text in every dimension. All 805 unit tests and 657 ordinary integration tests
passed (four opt-in skips). The full ordered database foundation and migration script passed,
including execution retries, finalization and runtime persistence. Release build had zero warnings
or errors. Final increment-5 documentation review remains pending.

### P7-MON-05 exit gate

Increment 5 is complete. The protected UI, monitoring health output and local structured logs
separate engine state from target health. Operational-state boundaries, scheduled-only freshness,
zero-work/failure/recovery heartbeats, real worker queue coverage, all-role route isolation and
metric cardinality have passing evidence above. The status and badge contract is consolidated in
General/Monitoring_Status_Reference.md. Desktop/mobile diagnostics, dashboard and endpoint output
were inspected. Database readiness remains independent. Retention and representative release
workloads remain pending; no production availability claim or external exporter is included.

### P7-DATA-01 bounded steady-failure evidence

Repeated confirmed failures with no severity escalation now leave incident evidence, events and
version unchanged. Every raw check remains recorded. The existing opening/recovery/resolution
flow is preserved. Retention_Runbook.md defines audit events as retained indefinitely before any
age-based deletion is introduced. No retention worker or deletion permission is enabled yet.

Verification on 2026-09-08: full ordered database foundation and migration script passed. The
health-confirmation stage now repeats three failures and asserts stable evidence/event counts
and incident version, then verifies recovery evidence and the exact material audit sequence.
All 657 ordinary integration tests passed (four opt-in skips). Release build: zero warnings/errors.
Hold/aggregate schema, deletion worker and the remainder of increment 6 are still pending.

### P7-DATA-01 retention hold storage

Migration 20260908144110_RetentionHolds adds all nine scope types, bounded reason, creator/time,
optional expiry and paired release actor/time. Database checks reject empty identities, unsupported
scopes, empty reasons and inconsistent dates/releases. Scope and actor identities are historical
identifiers; the upcoming service validates existing records and role access. No secondary indexes
or deletion worker are introduced. EF, compiled model and expected schema/entity lists are updated.

Verification on 2026-09-08: full ordered database foundation and explicit migration script passed.
The new schema checks exercise supported scopes, invalid combinations, microsecond expiry and
release boundaries, and isolated populated rollback/upgrade/repeatability. All 657 ordinary
integration tests passed (four opt-in skips). Release build had zero warnings/errors; EF reports
no pending model changes. Administrator management, scope expansion and hold enforcement remain
pending with the rest of increment 6. Setup and the retention runbook describe this limitation.

### P7-DATA-01 Administrator hold service

Hold listing is bounded to 100 rows with deterministic ordering. Creation and release require an
active Administrator, validate scope existence and commit lifecycle audit rows atomically. All
nine scope relationships are supported, including archived records. Expiry is normalized to UTC
microseconds before validation. Concurrent releases retain the first actor/time and one audit.
Hold changes acquire transaction advisory lock (761924, 1); retention batches must share this lock.
Audit snapshots exclude free-text reason. No additional migration or enabled deletion is required.

Verification on 2026-09-08: full ordered database foundation and migration script passed, including
role rejection, missing actor/scope rejection, reason bounds, sub-microsecond expiry rejection,
valid microsecond expiry, concurrent idempotent release and exact lifecycle audit records. All
657 ordinary integration tests passed (four opt-in skips). Release build had zero warnings/errors.
The Administrator web flow, scope expansion and deletion enforcement remain pending.

### P7-DATA-01 protected hold web flow

Administrators can create and release holds through /Retention, linked from protected monitoring
diagnostics. Forms use the existing shell and form/table styles. History is paged and distinguishes
active, expired and released records. The current form accepts a record ID from a detail address
or export. Expiry input is explicitly UTC. POST actions require antiforgery validation, all actions
require the Administration policy, and reason text is HTML-encoded.

Verification on 2026-09-08: ten direct web cases passed covering all four read roles, valid-token
mutation rejection for non-Administrators, missing-token rejection, reason encoding and UTC expiry.
All 667 ordinary integration tests passed (four opt-in skips). After the final history-label change,
the ten web cases passed again. Browser layout verification remains pending. Persistence/service
behavior has the prior full database evidence; no persistence change was introduced in this slice.
Worker enforcement, aggregates and the rest of increment 6 remain pending.

### P7-DATA-01 hold scope matching

RetentionHoldQueries composes active hold predicates through registry ancestry and incident
evidence without copying target data. It covers endpoints, monitors, checks, incidents, crawl
runs and PageAudit runs. Incident bundles preserve their referenced checks/PageAudit evidence;
holds on that evidence preserve the referencing bundle. Crawl-only holds stay limited to crawl
history. Expiry is exclusive at the exact timestamp; released holds stop matching immediately.

Verification on 2026-09-08: full ordered database foundation and migration script passed. A named,
transaction-owned fixture exercises every supported scope against actual PostgreSQL queries,
including parent propagation, incident evidence references, crawl isolation, exact microsecond
expiry and immediate release. The initial fixture omitted a required Lighthouse version; that
fixture was corrected before the green run. Release build had zero warnings/errors. No migration
or secondary index was added. Worker integration, aggregate storage/reporting and UI browser
verification remain pending; increment 6 is not complete and deletion remains disabled.

### P7-DATA-01 aggregate histogram contract

ResponseTimeHistogram defines version-1 non-cumulative duration buckets and approximate nearest-rank
percentiles capped by the recorded maximum. Empty samples return no estimate; invalid shape, counts,
durations and percentile inputs are rejected. The runbook records bucket bounds and the unchanged
eligible Healthy/Warning duration sample set before aggregate persistence and report integration.

Verification on 2026-09-08: ten focused boundary/estimate/validation cases passed, followed by all
815 unit tests. Release compilation succeeded. No current report calculation or database schema
changes in this slice. Aggregate storage, recomputation, older-window reporting and retention
worker integration remain pending; the overall goal is still in progress.

### P7-DATA-01 daily aggregate schema

Migration 20260908150752_MonitoringDailyAggregates adds a unique monitor/UTC-day summary with
sample counts, duration statistics and versioned histogram, comparability/source provenance,
measurement bounds and raw-deletion-start marker. Database constraints enforce count partitions,
histogram totals, valid duration ranges, UTC-day membership and marker ordering. The compiled
model and schema expectations are updated. Endpoint purge deletes aggregate rows before monitors.
No secondary index or automatic retention is introduced.

Verification on 2026-09-08: full ordered database foundation and migration script passed, including
round-trip fields, inconsistent-summary rejection, duplicate-day rejection, populated isolated
rollback/upgrade/repeatability and aggregate removal through endpoint purge. All 667 ordinary
integration tests passed (four opt-in skips). Release build had zero warnings/errors; EF reports
no pending model changes. Recompute writer, long-window report integration and deletion worker
remain pending; aggregate schema alone does not satisfy the retention exit gate.

### P7-DATA-01 aggregate recomputation

DailyAggregateWriter streams raw rows for one completed UTC day and atomically creates/replaces
its summary under the retention transaction lock. It preserves uptime and responded-duration
classification, computes deterministic snapshot comparability and refuses current/future/empty
days or aggregates whose raw deletion has started. It can participate in the worker transaction;
no automatic worker or report consumption is enabled by this slice.

Verification on 2026-09-08: full ordered database foundation and migration script passed after
adding late-data tests. Assertions prove exact eligible/excluded/outcome/duration totals, source
range, histogram counts, repeatability, late-result replacement, UTC midnight exclusion at the
microsecond boundary, mixed-generation provenance and refusal after the raw-deletion marker.
Release build had zero warnings/errors. Report integration, worker deletion and the remainder of
increment 6 remain pending; the overall goal is not complete.

### P7-DATA-01 transaction-local immutable deletion permission

Migration 20260908151843_MonitoringRetentionPermission permits snapshot, incident-event and
incident-evidence DELETE only under transaction-local monitoring_retention or existing endpoint
purge permission. UPDATE stays forbidden. Maintenance-occurrence behavior is unchanged. Down
restores endpoint-purge-only functions. No entity shape, secondary index or enabled job changes.

Verification on 2026-09-08: full ordered database foundation and migration script passed with
actual protected-row deletes, update rejection with permission enabled, maintenance delete/update
rejection, same-connection permission reset after rollback and commit, and isolated populated
Down/Up/repeatability checks. Existing endpoint purge coverage also passed. Release build had zero
warnings/errors and EF reported no pending model changes. Retention worker integration and report
consumption remain pending; increment 6 and the overall goal are not complete.

### P7-DATA-01 retention run limits

Monitoring:Retention now binds validated startup options with explicit disabled/dry-run defaults.
Batch size is bounded to 1..1000, batches per run to 1..20 and run duration to 1..30 seconds.
appsettings.json records the planned default limits; no deletion job is registered by this slice.

Verification on 2026-09-08: eight focused default/boundary cases and all 823 unit tests passed.
All 667 ordinary integration tests passed (four opt-in skips), including application startup with
the new configuration. No persistence changes were introduced. Worker implementation, report
integration and the remaining retention/release gates are still pending.

### P7-DATA-01 execution-attempt deletion batch

The first bounded deletion batch selects finished attempts/checks strictly older than 90 days,
excluding held/current-health/active-incident/leased checks. Each batch owns its transaction and
retention lock, supports disabled mode and dry-run, applies cancellation/runtime limits and logs
only operation/count/duration/dry-run facts. No recurring job or secondary index is introduced.

Verification on 2026-09-08: full ordered database foundation and migration script passed. Named
fixtures prove disabled/dry-run preservation, two one-row batches and an empty restart, the exact
90-day microsecond boundary, preservation of all protected categories and unfinished checks,
and cancellation. Fixture timestamp/snapshot requirements were corrected before the green run.
Release build had zero warnings/errors. Durable-work/raw-result/other category batches, run
coordination, report consumption and final workload/UI evidence remain pending.

### P7-DATA-01 completed durable-work retention

ExecutionHistoryRetentionBatch now shares attempt/work transaction and protected-check logic.
Completed durable work is selected only when its update and check completion are strictly older
than 90 days and all work lease fields are absent. Pending/failed/leased work remains. Both
execution-history deletion paths reapply eligibility for selected IDs during DELETE. No recurring
job, new migration or secondary index is introduced.

Verification on 2026-09-08: full ordered database foundation and migration script passed with
attempt regressions and durable-work disabled/dry-run, one-row batches, restart, protected records,
pending/failed/leased states and the independent work-update cutoff boundary. Release build had
zero warnings/errors. Other retention categories, run coordination, aggregate reporting and final
workload/browser evidence remain pending; the goal is still active.

### Raw-result retention batches

RawResultRetentionBatch selects one bounded monitor/day batch, writes the complete daily aggregate
before deleting findings, redirects and results, and preserves that aggregate across subsequent
batches. Shared completed-check eligibility protects holds, leases, current health and active
incident evidence. Retained SEO and certificate observations additionally preserve their results.
Logical checks and snapshots remain. Dry-run writes neither aggregates nor deletion markers.

Validation: the full database foundation script passed with a clean Release build (zero warnings
and errors). Its named execution-retention fixture proves the strict cutoff, protected rows,
BatchSize=1, dry-run, aggregate-before-delete, unchanged aggregate after resumed deletion, child
cleanup, check/snapshot survival and independent SEO/certificate reference protection. The batch
remains unscheduled. Observation retention, remaining categories, report integration, coordinator
and full acceptance/load gates are still pending; this is not completion of increment 6.

### Observation retention batches

ObservationRetentionBatch adds bounded SEO (90-day) and certificate (24-calendar-month) cleanup.
Shared completed-check protections apply, and the latest recorded plus latest Current result-backed
observation timestamps survive, including ties. This prevents a newer superseded observation from
causing deletion of the evidence current readers still display. Results and checks are left intact
for the aggregate-aware raw cleanup path.

Validation: the full database foundation script passed, including named fixtures for both categories.
Each proves disabled/dry-run behavior, BatchSize=1, exact cutoff survival, holds, tied current
baselines, preservation behind newer superseded evidence, aging across the cutoff, retained latest
superseded evidence, result/check survival and cancellation. Release build: zero warnings/errors.
Observation jobs remain unscheduled with retention disabled; increment 6 and the final gates remain
in progress.

### Crawl retention batches

CrawlRetentionBatch expires bounded terminal history strictly after 90 days, deleting links before
runs. It preserves active holds, running runs, the latest terminal run and the latest two complete
full-coverage comparison runs per endpoint. CrawlHistoryQueries shares comparison eligibility with
CrawlReportReader. Terminal execution claim IDs remain historical tokens and do not pin all history.

Validation: full database foundation script passed with a zero-warning/error Release build. The
named crawl fixture proves disabled/dry-run behavior, BatchSize=1, exact cutoff survival, held and
running survival, latest failed terminal survival, both comparison baselines, child cleanup and
cancellation. Retention remains disabled and unscheduled; remaining increment 6 work and final
acceptance/load gates are still pending.

### PageAudit retention batches

PageAuditRetentionBatch expires bounded terminal runs and their items strictly after 90 days.
Holds, active/leased runs, retained incident evidence references, the latest terminal run per target
and strategy, and scored comparison baselines per target/strategy/locale survive. Scored eligibility
is shared with PageAuditReader through PageAuditHistoryQueries. Targets and incident evidence stay
intact; terminal incident bundle cleanup remains a separate dependency.

Validation: full database foundation script passed with zero Release build warnings/errors. The
named fixture proves disabled/dry-run behavior, BatchSize=1, held and active leased survival, incident
reference preservation, two successful comparison baselines, a separate locale baseline, exact
cutoff/latest terminal survival after clock advancement, child cleanup and cancellation. Retention
remains disabled and unscheduled; increment 6 and final acceptance/load verification are incomplete.

### Logical-check cleanup

LogicalCheckRetentionBatch removes an expired completed check and its immutable snapshot only when
all retained children and incident references are absent. Shared hold, lease and current-evidence
protections remain in force. The snapshot and check disappear in one transaction while daily
aggregates remain, satisfying the deferred snapshot constraint at commit.

Validation: full database foundation script passed with zero Release warnings/errors. The existing
named execution fixture now independently pins an otherwise eligible check with SEO observations,
certificate observations and an execution attempt, then proves deletion after those references are
removed. Disabled/dry-run and bounded cleanup are verified; retained work, other checks/snapshots
and the daily aggregate survive. Retention remains disabled and unscheduled. Increment 6 and the
final acceptance/load gates remain incomplete.

### Aggregate expiration batches

AggregateRetentionBatch expires bounded single-monitor UTC dates older than 24 calendar months.
Cutoff-day aggregates, unsealed aggregates, days with retained raw results and held monitoring
history survive. Held logical checks conservatively preserve their monitor aggregates because a
precise measured day may be unavailable after raw expiration. Hold release restores eligibility.

Validation: full database foundation script passed with zero Release warnings/errors after fixing
an assertion collection type. The aggregate fixture proves disabled/dry-run behavior, BatchSize=1,
UTC raw-day protection, cutoff-day and unsealed survival, monitor/check holds and release, retained
raw-result survival and cancellation. Retention remains disabled and unscheduled. Remaining
increment 6 integration and the final acceptance/load gates are still incomplete.

### Robots cache retention

RobotsRetentionBatch expires old default-policy cache rows only after expiry, preserving fresh
snapshots, sitemap requirements/URLs, approved exceptions and held origins. The single-row-per-origin
schema has no separate superseded snapshot history; refresh replaces fetched contents in place.
Deletion shares the origin lock with refresh and rechecks eligibility. RelatedEndpointIds expands
all supported hold scopes to the protected origin.

Validation: full database foundation script passed with zero Release warnings/errors. The named
robots fixture proves disabled/dry-run behavior, BatchSize=1, fetch/update cutoff and expiry
boundaries, fresh/policy survival, held-origin survival and release, hostname-prefix separation and
cancellation. Scope regressions prove related endpoint protection and release for all nine hold
scopes. Retention remains disabled and unscheduled; remaining integration and final gates are pending.

### Terminal incident bundle retention

IncidentRetentionBatch expires Resolved/Closed bundles after 24 calendar months from their terminal
timestamp. Active/held incidents, pending/retrying/processing deliveries, delivery leases and held
successor links survive. Incident and delivery rows are locked before eligibility is rechecked.
Children are removed in foreign-key order in one transaction. Retained successors preserve their
RecurrenceCount, advance Version, and receive an audited PreviousIncidentId detachment.

Validation: the full database foundation script passed with zero Release warnings/errors. Named
fixtures prove bounded/dry-run deletion, closure cutoff, Resolved eligibility, active/held/pending/
leased survival, held predecessor protection, complete child removal and recurrence/audit correctness.
A controlled root-delete failure proves that child deletion, link detachment and audit writes all
roll back; retry then succeeds. Retention remains disabled and unscheduled. Coordinator/report
integration and final acceptance/load gates are still pending.

### Bounded hourly coordinator

MonitoringRetentionCoordinator connects eleven categories with one shared deadline, a hard attempted-
batch cap, dependency-order passes, empty-pass stopping and single-pass dry-run sampling. The runner
creates a fresh scope/context for every batch. MonitoringRetentionJob is a thin maintenance-queue
Hangfire entry point with automatic retries disabled and sanitized failure reporting. Enabling only
retention also registers Hangfire storage and a maintenance worker. Defaults remain disabled/dry-run.

Validation: 832 unit tests passed; 667 ordinary integration tests passed with four opt-in skips;
the full database foundation script passed independently. Coordinator regressions cover caps, pass
ordering, dry-run non-duplication, empty stopping, elapsed/in-flight deadlines, caller cancellation
and safe job failures. The database test verifies idempotent hourly registration, triggers a real
job and proves its Enqueued state targets maintenance, then resolves all eleven actual batches.
Hangfire's legacy recurring hash Queue field is default; the actual queued-state assertion is the
routing evidence. Aggregate-backed reporting, UI verification and final acceptance/load gates remain
pending, so deletion stays disabled outside controlled disposable tests.

### Retained reporting integration - 2026-09-08

- Reports use frozen aggregates only after raw deletion starts for a monitor/UTC day. Retained raw copies of archived days are excluded, preventing hold-related double counting; unsealed aggregates do not replace raw history.
- Summary, monitor rows and daily trends combine complete archived days with raw samples. Raw-only percentiles remain exact; mixed/archived response percentiles use merged fixed histogram counts and are identified as approximate in dashboard markup and CSV.
- Partial archived boundary days are omitted and disclosed rather than extrapolated. UTC-midnight boundaries include complete days. Comparability includes configuration fingerprint, snapshot schema and truth generation across raw and aggregated history.
- Database regression evidence covers unsealed aggregates, mixed counts, retained held raw records, exact and approximate percentiles, day trends, partial boundaries, a 366-day window, CSV disclosure and generation drift. The fixture also completes its expired logical-check cleanup so subsequent ordered stages remain independent.
- Verification: 832 unit tests passed; 667 ordinary integration tests passed with four opt-in skips; the full database-foundation script passed, including migration/bootstrap repeatability. Build completed with zero warnings/errors. Browser and representative performance gates remain outstanding.

### Retention worker activation and blocked-transaction cancellation - 2026-09-08

- The database-foundation coordinator fixture now starts the application's actual Hangfire host on an ephemeral loopback port. The registered hourly job is manually triggered, verified on the maintenance queue, and observed reaching Succeeded through the real worker and dependency-injection activation path.
- A separate PostgreSQL transaction holds the shared retention advisory lock. A one-second coordinator deadline interrupts the blocked batch with no completed category. After the blocking transaction rolls back, the queued job completes successfully. This covers database cancellation and subsequent execution, not process-crash recovery or loaded throughput.
- The complete database-foundation script passed with these checks, all later ordered stages, and explicit migration/bootstrap repeatability. Build: zero warnings and errors. The host is stopped and the triggered job/recurring registration are cleaned up by the fixture.
- Updated stale runbook status text to reflect the implemented coordinator, observation protection and aggregate-backed reporting. Full acceptance/load and browser gates remain outstanding; deletion stays disabled by default.

### Reporting performance gate and completion-index experiment - 2026-09-08

- Full baseline command: scripts/run-reporting-performance-baseline.ps1. The run completed all measured scenarios and FAILED the three-second dashboard gate. Fixture: 96 endpoints, 192 monitors, 1,667,520 results over 90 days; ten timed iterations per scenario. This is reporting evidence, not the required 500-endpoint monitoring-worker workload.
- Machine: Intel Core 5 210H, eight cores/twelve logical processors, 15.64 GiB physical memory; .NET SDK 10.0.400, PostgreSQL 18.1. Test duration was 22 minutes 23 seconds including fixture setup and plan capture. No third-party target was contacted by this reporting fixture.
- P95: unfiltered 30-day dashboard 15,169 ms; one-client 30-day dashboard 3,638 ms; unfiltered 90-day dashboard 26,729 ms; scoped-viewer dashboard 3,682 ms. CSV p95 was 7,777 ms and the 90-day dataset p95 was 24,455 ms. All dashboard scenarios exceeded the target.
- Complete captured application plans and timings: [Reporting_Baseline_Before_Optimization.md](Reporting_Baseline_Before_Optimization.md). Plans show repeated correlated completion-history scans and large external sorts for daily sample aggregation. These are measured optimization targets; the performance gate remains failed.
- Isolated SQL experiment on the same populated database: [SQL](Reporting_Completion_Index_Experiment.sql), [plans](Reporting_Completion_Index_Experiment.txt). The actual composite check/result join is retained. A filtered completion-order index plus ordered LIMIT 1 reduced the 192-monitor lookup from 3,176.601 ms to 30.986 ms. The experimental index was rolled back. This single-plan result supports implementation work but does not prove the dashboard budget passes.
- Next work: implement and regression-check the completion lookup/index, optimize the reporting aggregation paths using the captured plans, then rerun complete measurements. No performance threshold or workload requirement has been relaxed.

### Indexed scheduled-completion lookup - 2026-09-08

- Migration 20260908183513_ScheduledCompletionLookup adds a filtered (monitor, completion DESC, ID DESC) index for completed scheduled checks with non-null completion times. Reporting chooses the first current-result-backed check in that order. Null timestamps remain excluded, preserving the former Max semantics; manual, urgent and superseded checks remain ineligible.
- Diagnostics reuse these per-monitor values and take the maximum over the bounded monitor selection in memory. The initial attempt to aggregate the projected record in EF was rejected by the real database suite; the corrected implementation passed the complete suite, including existing scheduled-freshness and reporting checks.
- EF configuration, model snapshot, compiled model and expected migration list were updated together. No tables, columns, purge relationships or negative INSERT contracts changed. Setup instructions describe explicit migration application.
- Verification: clean build with zero warnings/errors; 832 unit tests and 667 ordinary integration tests passed (four opt-in skips); full ordered database-foundation and bootstrap repeatability passed after the translation correction.
- Explicit Up, Down to MonitoringRetentionPermission, and Up again all succeeded on the separate populated reporting database. All 1,667,520 check results remained, and pg_indexes confirmed the expected index definition. This database was not the shared database-foundation fixture.
- The prior index experiment provides the supporting EXPLAIN ANALYZE/BUFFERS evidence. End-to-end reporting performance still requires aggregation optimization and full remeasurement; the failed baseline is not superseded by these correctness checks.

### Native report grouping and conditional histograms - 2026-09-08

- Report SQL now groups by native UUID/date values and converts only output keys to text. This avoids sorting text representations of dates and IDs while preserving the existing report keys and UTC boundaries.
- Raw histogram filters run only when the same statement sees a covered archived day with response samples. Raw-only percentiles remain exact and do not consume a histogram; mixed response windows still calculate the raw histogram needed for merging. This condition shares the report statement's database snapshot.
- On the preserved 1,667,520-result fixture, EXPLAIN ANALYZE/BUFFERS measured the original 90-day trend query at 7,852.986 ms, native-date grouping at 6,473.554 ms, and native grouping with conditional histograms at 4,777.646 ms. Full plans: [Reporting_Grouping_Experiment.txt](Reporting_Grouping_Experiment.txt). These are sequential single-query observations, not end-to-end p95 measurements.
- Validation: zero-warning/error build and the complete database-foundation script passed, including raw-only/unsealed history, merged held/raw/archived counts and percentiles, daily trends, 366-day windows, partial archived boundaries, CSV and comparability checks, plus all later ordered stages and bootstrap repeatability.
- No schema or setup change is required. The three-second dashboard gate remains unproven and the failed full baseline remains authoritative until complete remeasurement passes.

### Reporting remeasurement workflow and rejected experiments - 2026-09-08

- The opt-in reporting benchmark accepts WEBHEALTH_BASELINE_REUSE_FIXTURE=1 when invoking its dotnet test directly with the existing connection/log/evidence variables. Default behavior still creates a fresh fixture. Reuse locates the named baseline actors/client, checks the monitor count and full cadence-derived sample count, and reruns relational history-integrity checks before measuring. It does not reduce windows, iterations or the three-second gate.
- The preserved fixture passed reuse validation and the full application benchmark reached report query execution. Build completed with zero warnings/errors. Complete timing results for this run remain pending; the previous failed baseline is still authoritative.
- Additional read-only SQL experiments were not adopted: frequency-based exact daily percentiles matched PostgreSQL with zero mismatches but measured 3,591.604 ms versus 4,020.209 ms; combined summary/day/monitor frequency grouping was slower (7,865.042 ms versus 6,497.632 ms) despite zero result mismatches; an empty-archive guard was slower (5,040.570 ms versus 4,812.602 ms); 64 MiB transaction-local sort memory measured 4,546.321 ms while using roughly 53-60 MiB per sort worker. These single-query observations do not justify extra algorithm or memory-budget complexity.
- No production query or database setting changed in this slice. The temporary sort setting rolled back. Full application remeasurement uses the committed completion-index and native-grouping/conditional-histogram changes only.

### Full reporting remeasurement after first optimizations - 2026-09-08

- The validated-reuse benchmark completed all six scenarios and FAILED the unchanged three-second dashboard gate. All 1,667,520 results and the original 192 monitors/windows/ten iterations were retained. Full report: [Reporting_Baseline_After_First_Optimizations.md](Reporting_Baseline_After_First_Optimizations.md).
- P95: default unfiltered 30-day dashboard 6,254 ms (previously 15,169); one-client dashboard 1,725 ms; scoped-viewer dashboard 1,695 ms; unfiltered 90-day dashboard 16,256 ms. CSV export p95 was 1,354 ms; the 90-day dataset p95 was 20,009 ms. Filtered dashboards now pass; both unfiltered dashboard cases still fail.
- Application plans confirm that the indexed completion projections now execute in single-digit milliseconds. Remaining 30-day costs include comparability at 2,501 ms, two raw-sample aggregates near 1,654-1,694 ms, and certificate selection near 618 ms. Plan durations include instrumentation and are not substitutes for end-to-end measurements.
- Comparability sorts about 547,293 eligible rows to return 96 identities and spills its hash join/sort to temporary storage. The snapshot side scans all 1,667,520 snapshot rows. These plans identify the next optimization targets. No acceptance threshold, reporting window, exact-raw requirement or workload size has been relaxed.

### Comparability identity grouping - 2026-09-09

- The raw comparability query groups by the existing monitor/fingerprint/schema/generation/source tuple before ordering the distinct identities. The tuple and ordering contract are unchanged; no identity hashing or source-comparability behavior changed.
- On the preserved reporting fixture, EXPLAIN ANALYZE/BUFFERS measured the original DISTINCT query at 1,754.186 ms and explicit grouping at 1,234.500 ms. A bidirectional EXCEPT ALL comparison returned zero mismatches. Plans: [Reporting_Comparability_Group_Experiment.txt](Reporting_Comparability_Group_Experiment.txt). These single-query timings do not establish dashboard p95.
- Covering-index experiments were rejected as a combined change: comparability worsened from 2,233.943 to 2,697.811 ms, while two sample queries changed from 1,606.328 to 1,126.488 ms and 1,682.492 to 1,586.495 ms. Both experimental indexes rolled back; no migration or setup change is introduced.
- Validation: clean build with zero warnings/errors and the full ordered database-foundation script passed, including mixed-history reporting, generation/configuration drift, CSV, all later stages and migration/bootstrap repeatability. Both disposable clusters are stopped. Full unfiltered-dashboard performance remains below the required standard and needs further work.

### Approximate trend tooltip disclosure - 2026-09-09

- Response-chart tooltips identify approximate histogram percentiles for each archived day. Exact raw days in the same window do not receive that label. Both P50 and P95 use the existing per-point approximation flag emitted by the Razor view; values and threshold lines are unchanged.
- Corrected the partial-day explanation from hourly detail to raw detail, matching what retention actually removes.
- Added one JavaScript regression for mixed approximate/exact days and older chart data without an approximation flag. Updated a stale 403-message assertion to check the existing role-denial and no-change message rather than requiring the absent word permission.
- Verification: all 39 JavaScript tests and syntax checks passed; all 48 ApplicationShellTests/AjaxContractTests passed after rebuilding the Razor view. Browser verification remains outstanding; these checks do not close the full UI or performance gates.

### Bounded incident evidence preserves severity escalation - 2026-09-09

- Added a database regression using one owned SSL monitor and one unchanged certificate. A warning opens an incident; changing the recorded expiry thresholds escalates the same incident to High and adds exactly one failure-evidence record plus the severity and evidence timeline events.
- Repeating the High observation and returning to Warning thresholds add no further evidence/events, do not increment the incident version, and do not lower its retained severity. This protects the escalation exception to repeated-failure evidence suppression through the actual execution/finalization and PostgreSQL persistence path.
- Validation: the full scripts/run-database-foundation-tests.ps1 passed, including every later ordered stage and explicit migration application. Release build had zero warnings/errors; the disposable PostgreSQL cluster shut down successfully. No application behavior, schema or setup change was needed.

### Concurrent incident reopening during retention - 2026-09-09

- Added a two-connection PostgreSQL regression for an expired incident selected while a concurrent transaction changes it back to Open. The test waits until pg_blocking_pids identifies the mutation connection as the retention connection's blocker, then commits the reopening.
- Cleanup must recheck eligibility after obtaining the incident row lock and return zero selected/deleted roots. The reopened incident, its evidence, timeline event and notification attempt remain intact. The fixture is restored afterward so the existing rollback, eligible-bundle deletion and lineage checks still execute.
- Validation: the full database-foundation script passed with the explicit blocker assertion, all later ordered stages and explicit migrations. Release build had zero warnings/errors and the disposable cluster stopped. This verifies the database mutation race; it does not replace outstanding process-interruption, notification-claim concurrency or load/throughput gates.

### Retention during notification dispatch and retry - 2026-09-09

- Added a real NotificationDispatchService/PostgreSQL concurrency regression for the owned expired incident with a pending delivery. A controlled email transport pauses after the dispatcher commits its Processing lease; retention runs on a separate connection and preserves the bundle and evidence.
- After the transport returns a transient failure, the dispatcher records the second attempt, releases the lease and schedules a retry. Another retention pass preserves the incident and delivery. No email is sent and no dispatcher, claim query or persistence behavior is mocked.
- The claim query only selects Pending/RetryScheduled or expired Processing deliveries, all of which the retention candidate query excludes. The regression exercises that supported transition instead of manufacturing a Sent-to-Processing transition unavailable through the dispatcher.
- Validation: full database-foundation script passed, including all later ordered stages and explicit migrations; zero build warnings/errors; disposable PostgreSQL cluster stopped. Process-crash recovery and measured load/throughput remain outstanding.

### P7-DATA-01 AC-14 acceptance and reporting performance - 2026-09-09

- The twelve-category coordinator prepares exact completed-day aggregates before running all eleven deletion categories. Every category shares the transaction advisory lock, bounded selection, cancellation deadline and dry-run contract. A blocked preparation attempt times out without reporting a completed category; the subsequent scheduled execution succeeds after the lock is released.
- Completed days store exact responded-duration samples while raw history remains. Reports load aggregate counts and compact duration arrays, query raw rows only for missing monitor-day ranges and calculate exact percentiles in the application. Starting raw deletion clears the exact array atomically and retains the histogram, so post-deletion percentiles remain explicitly approximate.
- The clean-slate PostgreSQL 18 performance harness created 96 endpoints, 192 monitors, 1,667,520 results and 17,280 daily aggregates. Ten measured iterations produced dashboard p95 values of 639 ms for the unfiltered 30-day window, 102 ms for one client, 1,310 ms for the unfiltered 90-day window and 119 ms for a viewer scoped to one client. CSV 30-day p95 was 202 ms and the 90-day dataset p95 was 1,415 ms. All dashboard scenarios passed NFR-02 without a new index.
- The ordered database-foundation suite passed all 22 stages. It verifies strict cutoffs, dry-run, batch bounds, cancellation, restart after interruption, immutable-history permissions, all hold scopes, active/current/held survival, comparison baselines, aggregate-before-delete, 366-day mixed reporting, incident recurrence detachment and safe audit history.
- Administrator browser verification covered the retention page at the desktop viewport and 390 by 844 pixels. The page uses the dashboard shell, exposes all nine labeled scope choices, has no horizontal overflow and no empty validation alert. Direct integration requests continue to return 403 for Operations, Developer/Support and Viewer, and mutation requests require antiforgery tokens.
- AC-14 is satisfied for the implemented personal-project retention policy. Retention remains disabled and dry-run by default; commissioning requires explicit configuration and review of dry-run counts. Representative 500-endpoint load, failure injection and memory-window evidence remain in P7-MON-07.

## P7-MON-07 — representative hardening and release evidence

The controlled 500-endpoint load, enqueue interruption, PostgreSQL stop/restart recovery, bounded concurrency, two 15-minute memory windows, complete delivery checks and final limitations are recorded in [the Phase 7 gate](Phase_7_Gate.md). AC-15 passed on 2026-09-09. The monitoring hardening plan is complete for the personal internship and portfolio scope.

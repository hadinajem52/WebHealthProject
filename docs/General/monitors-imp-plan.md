# Phase 7 HTTP and SSL Monitoring Hardening Plan

**Status:** In progress

P7-MON-01 through P7-MON-03 implementation and verification are recorded in
[Monitoring hardening evidence](../phase-7/Monitoring_Hardening_Evidence.md).
P7-MON-04 through P7-MON-07 remain pending.

**Project profile:** Personal internship/portfolio project owned, implemented, reviewed, and operated by one intern.

## 1. Purpose

This plan hardens the existing HTTP availability and SSL certificate monitoring implementation and supplies part of the Phase 7 evidence required by the project specification.

It is not a production-certification program. It must not be used to describe WebHealth as enterprise-grade, independently audited, production-certified, highly available, or ready for an unspecified public deployment.

The target outcome is professionally correct monitoring behavior, demonstrable security and failure handling, bounded local/demo operation at representative portfolio scale, documented limitations and evidence, and a foundation that can be extended if the intern later chooses a real deployment.

Production hosting, HA, PITR, managed secret vaults, centralized alerting, formal on-call ownership, production rollout governance, and restore drills remain optional deployment work.

## 2. Relationship to the project roadmap

This document is a Phase 7 subplan. It does not replace:

- `docs/Website_Health_Monitoring_Project_Specification.md`;
- `docs/General/Phased_Implementation_Plan.md`;
- `docs/General/Detailed_Implementation_Plan.md`;
- the Phase 7 retention, hold, aggregate, security, performance, and release gates.

The repository has completed the main Phase 1 through Phase 6 feature increments. The current monitoring architecture is preserved:

```text
EndpointMonitor
    -> LogicalCheck
    -> immutable configuration snapshot
    -> DurableWork
    -> Hangfire
    -> ExecutionLease
    -> HTTP transport or SSL probe
    -> finalization
    -> result, health, incident, notification, and reporting
```

The increments below are reviewable slices for the sole owner. They do not imply separate teams, committees, external approvals, or production operations.

## 3. Scope

### 3.1 Included

- BR-S01 through BR-S08 scheduling and execution regression coverage.
- BR-H01 through BR-H10 HTTP correctness and configuration completion.
- BR-C01 through BR-C07 SSL correctness and security.
- BR-Q01, BR-Q02, BR-Q04, and BR-Q07 outbound-network hardening.
- BR-R01 through BR-R03 reporting compatibility.
- BR-R05 and BR-R06 Phase 7 retention, aggregate, and hold behavior.
- AC-02, AC-03, AC-05, AC-06, AC-11, AC-12, AC-14, and AC-15 evidence where affected.
- Monitoring-specific diagnostics, heartbeat, queue age, overdue work, and structured logging.
- Representative 500-endpoint portfolio-scale verification.

### 3.2 Excluded

- Multi-region monitoring.
- Browser journeys or scripted synthetic transactions.
- POST checks, arbitrary request bodies, authentication, or arbitrary headers.
- TLS cipher grading.
- HTTP/3-specific behavior.
- Distributed tracing.
- Database partitioning.
- Scheduler replacement.
- Certificate-controlled CRL, OCSP, or AIA downloads.
- A production readiness or production certification claim.
- Optional real-hosting operations unless separately authorized and planned.

## 4. Fixed architecture decisions

### 4.1 Snapshot v2 freezes the contacted target and policy

A logical check represents the target and policy that existed when the check was created.

Introduce:

```text
ResolvedMonitoringTarget
    EndpointId
    NormalizedUrl
    NormalizedHost
    EffectivePort
    NormalizationVersion
    IsProduction

ResolvedHttpCheckConfiguration
    Target
    Policy
    ConfigurationFingerprint
    CurrentTruthGeneration

ResolvedSslCheckConfiguration
    Target
    Policy
    ConfigurationFingerprint
    CurrentTruthGeneration
```

Snapshot v2 adds:

```text
target_normalized_url
target_normalized_host
target_effective_port
target_normalization_version
target_is_production
current_truth_generation
```

For v2 checks, execution must not use the current endpoint URL, host, port, or production flag to decide what to contact. The current registry may still be read for current authorization, lifecycle eligibility, and stale-result detection.

### 4.2 Current authorization remains authoritative

Target permission is never frozen into a snapshot. Immediately before every connection, including each redirect:

```text
snapshotted endpoint ID, host, and port
    + current TargetAuthorizationEvidence
    -> authorized or rejected
```

Revoked or expired authorization produces zero outbound connections. Authorization evidence, reasons, and references are never copied into snapshots or logs.

### 4.3 Current-truth generation closes lifecycle races

Add a monotonic `CurrentTruthGeneration` to `EndpointMonitor`. It changes only when a mutation can make queued evidence stale for current-state purposes. It does not change when dispatch advances `NextDueAt`.

Advance it in the same transaction when any of these change:

- endpoint URL, host, port, normalization version, or production classification;
- effective monitor policy or fingerprint;
- endpoint or monitor enabled state;
- scheduling mode or pause state;
- monitor archive, restore, retirement, replacement, or reconciliation;
- client, website, environment, or endpoint lifecycle eligibility affecting the monitor.

Parent lifecycle mutations update descendant active monitor generations with one bounded set-based statement in the same transaction. Every v2 snapshot captures the current generation.

Before mutating current health, issue counters, incidents, notifications, or urgent SSL scheduling, finalization verifies:

```text
snapshot monitor ID is the active monitor identity
AND snapshot current-truth generation equals current monitor generation
AND snapshot configuration fingerprint equals current monitor fingerprint
AND current lifecycle eligibility permits current-state mutation
```

If any check fails:

- persist the immutable historical result and applicable observation;
- mark the result superseded for current-state purposes;
- do not update endpoint health or issue counters;
- do not open, escalate, recover, or resolve incidents;
- do not emit notifications;
- do not schedule urgent work.

Archive-then-restore and pause-then-resume therefore cannot make an older check current again, even when the same monitor row and fingerprint are reused.

### 4.4 Snapshot compatibility is explicit

```text
schema_version = 1 -> legacy compatibility path
schema_version = 2 -> immutable target, policy, and generation path
```

Change the database constraint to `schema_version IN (1, 2)`. Completed v1 snapshots are not rewritten. Queued v1 checks may complete through the existing compatibility path during the local upgrade, but finalization still fails closed when the current monitor fingerprint differs.

After the migration and application are installed, every newly created HTTP or SSL check uses v2.

### 4.5 Configuration remains simple and explicit

For this personal project, do not turn `PolicyProfile` into a new runtime administration system.

Use:

```text
documented system defaults
    + typed EndpointMonitor.BoundedOverrides
    -> resolved effective configuration
    -> materialized EndpointMonitor columns
```

`PolicyProfileId` remains the policy identity and provenance. Existing empty `PolicyProfile.BoundedSettings` remains compatible. Mutable profile administration and automatic propagation are deferred.

Materialized columns are the current effective configuration used to schedule and validate new snapshots. Registry mutations update typed overrides, materialized values, fingerprint, and current-truth generation atomically.

For typed v2 overrides, disagreement between resolution and materialized values is configuration drift. Drift fails closed before creating a check and emits a structured operational error without URLs or marker contents.

### 4.6 Confirmed health and operational state are separate projections

Confirmed health is:

```text
Healthy
Warning
Critical
Unknown
```

Operational state is:

```text
Active
ManualOnly
Paused
Disabled
Delayed
Stale
NeverChecked
```

The application currently derives `Disabled` as a display status; normal finalization does not persist it as confirmed health. Do not assume a legacy-data backfill is needed without first querying the test database.

Update the specification and reporting contract deliberately when this separation is implemented. Keep compatibility mapping for any existing database row containing confirmed health `Disabled`, but do not create new such rows.

### 4.7 Certificate processing performs no certificate-controlled networking

Both normal HTTPS monitoring and the inspection probe use supported .NET 10 TLS settings equivalent to:

```text
CertificateRevocationCheckMode = NoCheck
DisableCertificateDownloads = true
```

No CRL, OCSP, or AIA URI from a certificate may cause an independent request. Revocation is reported as not checked; do not add `Good` or `Revoked` persistence values when the application cannot observe them.

### 4.8 Retention follows the approved project policy

The defaults remain:

- raw check and other high-volume raw execution detail: 90 days;
- daily aggregates: 24 months;
- terminal incidents and their notification bundle: 24 months;
- active incidents and held data: retained regardless of age.

The maximum 366-day reporting window uses daily aggregates after raw detail expires. Long-window response percentiles derived from fixed histogram buckets are labeled approximate in the UI and CSV metadata; raw-window percentiles remain exact.

Changing these periods requires a recorded BR-R05 decision and updates to the specification, traceability matrix, tests, and UI wording.

## 5. Increment 1 — Immediate monitoring correctness

**Work item:** P7-MON-01  
**Priority:** P0 correctness and security

### 5.1 Behavior

#### Multi-address DNS fallback

Refactor `SafeDestinationConnector.ConnectAsync`:

```text
resolve raw answers
    -> reject zero answers
    -> enforce MaxDnsAnswers on the raw result
    -> normalize IPv4-mapped IPv6
    -> deduplicate while preserving resolver order
    -> validate every answer
    -> reject the destination if any answer is prohibited
    -> attempt allowed addresses sequentially
    -> acquire a per-IP lease for each attempt
    -> create a fresh socket
    -> apply a bounded per-address timeout
    -> verify the connected peer
    -> return the first successful connection
```

Add:

```text
Monitoring:HttpTransport:PerAddressConnectTimeout
Default: 5 seconds
Bounds: 1 to 10 seconds
```

Overall request cancellation remains the absolute deadline. A per-address timeout advances to the next candidate; caller or overall cancellation does not.

Failure mapping remains deterministic:

```text
unsafe answer -> DestinationPolicy
DNS failure -> Dns
all candidates fail -> Connection
overall deadline -> Timeout
caller cancellation -> Cancellation
```

#### Monitor reconciliation

Create `EndpointMonitorReconciler` and use it from endpoint archive, restore, and URL-scheme changes.

Archive captures one timestamp and actor. It retires only active monitors and never overwrites earlier retirement metadata. Archive retirement preserves scheduling mode, pause state, cadence anchor, and due time.

Restore considers only monitors retired by the matching endpoint archive operation. Increment the current-truth generation before reactivation.

For HTTP, reactivate at most one compatible archive-retired monitor ordered by `CreatedAt DESC, Id ASC`, or create one through the `DbSet` if none exists.

For HTTPS, reconcile one compatible SSL monitor for the current host, port, and endpoint identity. For HTTP, leave every SSL monitor retired.

PageAudit identity remains owned by the PageAudit configuration path and is never restored through HTTP or SSL reconciliation.

#### Type-safe updates and dispatch

Use exhaustive switches:

```text
HttpAvailability -> HTTP update and HTTP transport
SslCertificate -> SSL update and SSL probe
PageAudit -> no HTTP or SSL mutation
unknown -> throw, roll back, and perform zero network access
```

`MonitorWorkKinds.For` already fails closed. The execution switch adds defense in depth.

### 5.2 Authorization and user-visible behavior

- Existing role and assignment rules remain unchanged.
- Archive and restore remain authorized registry operations.
- Restoring an endpoint does not silently enable it.
- Manual-only and paused choices survive archive and restore.
- Unknown types show a safe operational failure and perform zero outbound requests.

### 5.3 Data, security, and migration impact

- Prefer application-only changes in this increment.
- Any schema change is backward-compatible and updates every database-foundation expected migration, table, and column assertion.
- New entities with client-generated keys are added through their `DbSet`.
- Validate every DNS answer and actual peer address.
- Never log URL query strings, authorization evidence, addresses, hostnames, or response bodies.
- Preserve global, per-host, and per-IP limits across fallback attempts.

### 5.4 Tests and evidence

- First address fails and the second succeeds.
- IPv6 to IPv4 fallback and duplicate normalization.
- Mixed public/private answers reject before connection.
- Raw-answer limit, peer mismatch, and address-policy recheck.
- Per-IP lease disposal on success, failure, timeout, and cancellation.
- Historical monitors are not resurrected.
- Manual-only and paused state survives archive/restore.
- HTTP update does not change PageAudit or SSL fields.
- Unknown monitor produces zero outbound connections.
- Direct authorization tests remain green.
- Update Phase 7 evidence and source-tree documentation for new services.

### 5.5 Exit gate

DNS fallback is SSRF-safe, historical monitors stay historical, operator choices survive archive/restore, and monitor types cannot mutate or execute as one another.

## 6. Increment 2 — Snapshot v2 and stale-result protection

**Work item:** P7-MON-02  
**Priority:** P0 correctness

### 6.1 Behavior

- Add v2 target and current-truth generation fields.
- Add `EndpointMonitor.CurrentTruthGeneration` with a positive constraint.
- Centralize generation advancement and use it from endpoint and inherited lifecycle mutations.
- Create one shared target and snapshot builder for scheduled, manual, and urgent checks.
- Execute HTTP and SSL v2 checks entirely from the snapshot target.
- Validate transport evidence against the same snapshot.
- Classify stale results as immutable historical evidence that cannot become current truth.

Scheduled and manual creation for identical target and policy inputs produce equivalent configuration payloads other than check identity, source, actor, and timestamps.

### 6.2 Authorization and user-visible behavior

- Current authorization is checked before network access.
- A revoked old target is not contacted.
- A stale result remains visible in history with a `Superseded` disposition.
- Endpoint detail explains that superseded evidence did not change current health or incidents.
- No user role is introduced.

### 6.3 Data and migration impact

Add:

```text
endpoint_monitor.current_truth_generation

check_configuration_snapshot.target_normalized_url
check_configuration_snapshot.target_normalized_host
check_configuration_snapshot.target_effective_port
check_configuration_snapshot.target_normalization_version
check_configuration_snapshot.target_is_production
check_configuration_snapshot.current_truth_generation

check_result.current_state_disposition
```

Allowed dispositions are `Current`, `Superseded`, and `Ineligible`. For v2 snapshots, target and generation fields are mandatory. Completed v1 snapshots remain unchanged.

The migration updates the EF model, model snapshot, compiled model, expected migration/table/column/constraint lists, affected negative-test INSERT statements, and clean creation, upgrade, repeatability, and `Down` evidence.

Snapshot URLs receive the endpoint URL privacy treatment. Logs use identifiers, never full URLs.

### 6.4 Tests and evidence

- URL A queued, changed to B, old check never contacts B.
- Old A authorization revoked, zero connection to A.
- Policy change while queued.
- Pause then resume while queued.
- Disable then enable while queued.
- Archive then restore while queued.
- HTTP to HTTPS and HTTPS to HTTP while queued.
- SSL host or port change while queued.
- Parent lifecycle disable then re-enable while queued.
- Stale result persists but cannot change health, counters, incidents, notifications, or urgent work.
- v1 compatibility and v2 constraint tests.
- Timestamp assertions truncate to PostgreSQL microseconds.
- Update monitoring architecture and traceability documentation.

### 6.5 Exit gate

Every new check is reproducible from its v2 snapshot and network evidence, and no older lifecycle generation can become current again.

## 7. Increment 3 — Complete HTTP policy configuration

**Work item:** P7-MON-03  
**Priority:** P1 feature completion

### 7.1 Typed overrides

Replace interval-only JSON with:

```text
HttpMonitorOverridesV2
    SchemaVersion = 2
    IntervalSeconds?
    TimeoutSeconds?
    FailureConfirmationCount?
    RecoveryConfirmationCount?
    WarningThresholdMs?
    CriticalThresholdMs?
    AdditionalAcceptedStatusCodes?
    RequiredContentMarker?
    ContentMarkerComparison?
```

Internal transport limits remain application defaults rather than user-editable endpoint fields: `MaxResponseBodyBytes`, `MaxRedirects`, and `ProductionHttpSeverity`.

### 7.2 Defaults and bounds

| Setting | Rule |
|---|---|
| Production interval | 5 minutes |
| Non-production interval | 15 minutes |
| Timeout | 15 seconds by default; 1 to 120 seconds |
| Failure confirmation | 2 by default; 1 to 10 |
| Recovery confirmation | 2 by default; 1 to 10 |
| Warning threshold | 1,500 ms by default; at least 1 ms |
| Critical threshold | 3,000 ms by default; at or above warning and no greater than timeout |
| Additional accepted statuses | 300 to 499, distinct, maximum 20 |
| Required marker | Optional, maximum 500 characters |
| Comparison | Ordinal or OrdinalIgnoreCase |

All 2xx responses remain accepted. A configured 5xx can never be healthy. An accepted 3xx does not bypass redirect-loop, invalid-location, excessive-redirect, authorization, or HTTPS-policy findings.

Existing monitors using the current 30-second effective timeout retain 30 seconds through a canonical explicit override during migration. New monitors and monitors reset to defaults use the specification's 15-second default.

### 7.3 Resolution, fingerprint, and execution

Create one `IHttpMonitoringPolicyResolver`:

```text
documented defaults
    + validated typed overrides
    -> effective policy
    -> materialized-value comparison
    -> ResolvedHttpCheckConfiguration
```

The fingerprint includes normalized URL, HTTP type, production flag, and every resolved field. Continue the existing canonical implementation with an explicit version.

Decode required-marker matching only from the bounded response buffer. Support UTF-8, US-ASCII, and ISO-8859-1. Missing, malformed, or unsupported charset falls back to UTF-8. Do not reread or persist content.

### 7.4 Authorization and user-visible behavior

- Administrator-only fields remain enforced server-side.
- Endpoint forms expose supported overrides with field validation.
- Validation errors do not partially save configuration.
- Audit snapshots record safe policy facts, never marker text.
- Endpoint detail shows each effective value and its default/override source.

### 7.5 Data, tests, logging, and documentation

- Canonicalize active HTTP override JSON to v2.
- Preserve effective behavior when historical explicit/default intent cannot be recovered.
- Keep `{}` and interval-only JSON readable during compatibility.
- Update materialized values, JSON, fingerprint, and generation atomically.
- Test every boundary, invalid combination, canonical status ordering, 2xx/5xx rules, redirect security, charsets, bounded bodies, drift, snapshot equivalence, 30-second migration preservation, 15-second reset default, authorization, and anti-forgery.
- Log safe validation categories and drift identifiers; never marker text.
- Update BR-H02, BR-H06, BR-H09, Appendix A, UI documentation, audit evidence, compiled models, and database assertions.

### 7.6 Exit gate

Every new HTTP result can be explained from snapshot v2 and bounded network evidence without reading current target or policy values.

## 8. Increment 4 — SSL correctness and security

**Work item:** P7-MON-04  
**Priority:** P0 security and P1 correctness

### 8.1 Resolved policy and snapshot

Create:

```text
ResolvedSslPolicy
    IntervalSeconds = 86400
    TimeoutSeconds = 15
    FailureConfirmationCount = 1
    RecoveryConfirmationCount = 1
    WarningExpiryDays = 30
    HighExpiryDays = 15
    CriticalExpiryDays = 7
```

Validate `Warning > High > Critical >= 0`.

The SSL fingerprint uses an SSL-specific canonical prefix and includes target identity, production flag, cadence, timeout, confirmation counts, and expiry thresholds. Identify legacy fingerprints by comparing them with the computed expected legacy hash.

Add `ssl_warning_expiry_days`, `ssl_high_expiry_days`, and `ssl_critical_expiry_days` to v2 snapshots.

### 8.2 Security and structured facts

Configure the normal HTTPS handler and SSL probe so revocation and certificate downloads cannot create independent requests. Keep invalid-certificate inspection separate from application traffic and never accept an invalid certificate.

Extend `CertificateObservation` with:

```text
ValidityStatus: Valid, Expired, NotYetValid
HostnameStatus: Matched, Mismatched
ChainTrustStatus: Trusted, Untrusted, Unknown
ChainStatusCodes: canonical JSON array
```

Leaf validity is separate from chain trust. Canonicalize chain codes by expanding flags, removing `NoError`, deduplicating, ordinal sorting, and limiting to 32 names.

Do not store DER bytes, chain bytes, certificate-controlled URLs, or false revocation results. The UI states `Revocation not checked` as a capability note.

### 8.3 Multiple findings

Evaluate `Ssl.Expiry`, `Ssl.HostnameMismatch`, `Ssl.Untrusted`, and `Ssl.NotYetValid` independently. One observation may create several findings.

The singular display category uses:

```text
NotYetValid
Expired
HostnameMismatch
Untrusted
ExpiringSoon
```

A primary category never suppresses another finding.

### 8.4 Authorization, data, tests, and evidence

- Existing endpoint visibility and manual-check authorization remain unchanged.
- SSL detail shows leaf validity, hostname, chain trust, days, expiry, and the revocation limitation.
- Backfill only safely derivable facts and use `Unknown` where ambiguous.
- Never fabricate revocation, chain, or hostname facts.
- Test validity boundaries, expired intermediates, simultaneous faults, deterministic category, renewal, urgent checks, zero certificate-controlled networking, production TLS validation, and absence of certificate bytes in storage/logs.
- Update failure-category constraints, compiled models, database assertions, SSL evidence, limitations, and BR-C01 through BR-C07 traceability.

### 8.5 Exit gate

Certificate-controlled outbound requests equal zero, simultaneous faults remain visible, and no invalid certificate is accepted for application traffic.

## 9. Increment 5 — Operational state and monitoring diagnostics

**Work item:** P7-MON-05  
**Priority:** P1 operational correctness

### 9.1 Operational state and freshness

Base precedence:

```text
lifecycle ineligible -> Disabled
SchedulingEnabled = false -> ManualOnly
SchedulingEnabled = true and IsEnabled = false -> Paused
otherwise -> Active
```

Only completed scheduled checks refresh scheduled freshness:

```text
freshness grace = max(10 minutes, interval / 4)
stale after = interval + freshness grace
```

Final precedence is `Disabled`, `ManualOnly`, `Paused`, `Delayed`, `NeverChecked`, `Stale`, `Active`. Keep confirmed health and underlying operational dimensions separately available.

### 9.2 Runtime diagnostics

A monitor is delayed when eligible, scheduled, enabled, and overdue by more than:

```text
Monitoring:Scheduling:DispatchDelayGrace
Default: 10 minutes
Bounds: 2 to 30 minutes
```

Add `monitoring_runtime_state` rows for `monitoring-dispatch` and `monitoring-reconciliation`. Store last start, success, failure, duration, bounded failure category, and consecutive failures. Every invocation updates its row, including zero-work invocations.

Add a Hangfire adapter that reports server presence, short-check queue coverage, and recent worker heartbeat. Keep Hangfire DTOs behind it.

Expose authorized aggregate monitor/work counts, oldest overdue monitor, oldest queued work, and last scheduled completion through the existing reporting selection and authorization core.

### 9.3 Health thresholds

```text
scheduler warning age = 3 minutes
scheduler critical age = 5 minutes
worker heartbeat tolerance = 2 minutes
queue warning age = 5 minutes
queue critical age = 15 minutes
overdue warning age = DispatchDelayGrace
overdue critical age = 30 minutes
critical consecutive failures = 3
```

Monitoring health is separate from readiness. A monitored website being unavailable never makes `/health/ready` unhealthy. Disabled scheduling reports `Healthy` with `DisabledByConfiguration` detail.

### 9.4 Proportionate telemetry

Use structured Serilog events and `System.Diagnostics.Metrics` with only `monitor_type`, `source`, `failure_category`, and `operation` dimensions. Never use endpoint, monitor, check, URL, or hostname as dimensions.

External OpenTelemetry/OTLP export is not required for the personal local/demo baseline. Add it only for an explicitly chosen deployment or portfolio demonstration. Lack of an exporter does not block Phase 7.

### 9.5 Authorization, data, tests, and evidence

- Authorized callers see confirmed health and operational state separately.
- Detailed diagnostics and Hangfire remain protected.
- Viewer output excludes worker IDs, target hosts, and exception details.
- Add `monitoring_runtime_state` without fake heartbeat rows; derive operational state rather than storing it.
- Query for persisted `Disabled` health before deciding on compatibility data updates.
- Test precedence, freshness sources, boundary ages, heartbeat success/failure/zero-work/recovery, worker absence, disabled scheduling, readiness separation, role isolation, and metric cardinality.
- Execution scopes include check/work/endpoint/monitor/type/attempt/job/worker/source/schema/generation identifiers but never bodies, markers, query strings, authorization evidence, certificates, or secrets.
- Update status specification, badge reference, report/CSV contract, dashboard, endpoint detail, and limitations.

### 9.6 Exit gate

The intern can distinguish target failure from monitoring-engine failure through the protected UI, health output, and structured local logs without manually querying PostgreSQL.

## 10. Increment 6 — Project-wide retention, aggregates, and holds

**Work item:** P7-DATA-01  
**Priority:** P1 Phase 7 acceptance

This increment is project-wide because retention cannot ignore incidents, PageAudit, crawl, robots, notifications, maintenance references, or holds.

### 10.1 Default policy

| Data | Default |
|---|---|
| Raw logical-check results and findings | 90 days |
| Execution attempts and completed durable work | 90 days |
| Crawl runs and links | 90 days; retain latest terminal run per endpoint |
| PageAudit runs and items | 90 days; retain latest terminal run per target and strategy |
| Superseded robots snapshots | 90 days; retain current unexpired snapshot |
| Certificate observations | 24 months |
| Daily monitoring aggregates | 24 months |
| Terminal incident/event/evidence/notification bundle | 24 months after terminal closure |
| Audit events | Defined before implementation and not shorter than the history they explain |
| Active incidents and held records | No age-based deletion |

### 10.2 Daily aggregates

Add one aggregate per monitor and UTC date containing scheduled eligible/healthy/warning/down/maintenance/cancelled/excluded counts; duration count, sum, minimum, maximum; fixed response-time histogram buckets; comparability identity; and first/last timestamps.

Aggregate creation is idempotent and recomputable while raw detail exists. Aggregate completion precedes raw-day deletion. Reports use exact raw data where retained and aggregates for older dates. UI and CSV disclose approximate long-window percentiles and mixed raw/aggregate windows.

### 10.3 Holds

Add bounded retention holds with scope type/ID, reason, creator/time, optional expiry, and release actor/time. Supported scopes are client, website, environment, endpoint, monitor, logical check, incident, crawl run, and PageAudit run.

Only Administrators create or release holds. Hold checks expand through existing relationships without copying sensitive data.

### 10.4 Reference-aware deletion

`LogicalCheck` and its snapshot remain until no retained child, hold, or current reference needs them. Never delete checks referenced by active incident evidence, current health evidence, retained observations/results/findings/redirects/SEO/attempts/work/leases, holds, or any foreign-key child.

Repeated confirmed failure with no transition and no severity escalation stops creating incident evidence. Evidence remains for opening, escalation, recovery start, recovery interruption, resolution, and certificate replacement/supersession.

### 10.5 Immutable deletion and incident bundles

Add transaction-local retention permission:

```text
SET LOCAL web_health.monitoring_retention = 'on'
```

Snapshot, incident-event, and incident-evidence triggers allow delete only when endpoint purge or monitoring retention is enabled locally. Update rejection is unchanged. Do not broaden maintenance-occurrence exemptions.

Delete only terminal, expired, unheld incident bundles in FK-safe order. If a retained incident references an expired predecessor, detach `PreviousIncidentId`, preserve `RecurrenceCount`, and audit the retention lineage truncation.

### 10.6 Bounded worker

```text
Enabled = false
DryRun = true
BatchSize = 1000
MaximumBatchesPerRun = 20
MaximumRunDuration = 30 seconds
Schedule = hourly
```

Each batch owns one transaction, sets local permission, selects explicit IDs in deterministic order, deletes children before parents, commits, records counts, and checks cancellation/runtime. Dry-run selects and counts but deletes nothing.

Do not add indexes without representative `EXPLAIN (ANALYZE, BUFFERS)` evidence.

### 10.7 Authorization, migration, tests, and evidence

- Only Administrators configure retention and holds.
- Reports continue resolving historical names and disclose raw versus aggregate data.
- Update trigger functions, purge graphs, EF/compiled models, expected migration/table/column lists, constraints, and negative INSERTs.
- Validate `Down` against data allowed by `Up`; upgrade/repeatability use isolated databases.
- Test microsecond cutoffs, batches, runtime, cancellation, restart, dry-run, active/current/held survival, aggregate-before-delete, 366-day reporting, approximate labels, crawl/PageAudit baselines, robots current snapshot, certificate/incident boundaries, trigger permissions, bounded incident evidence, bundle cleanup, recurrence detachment, permission reset after transaction, and measured steady-state throughput.
- Log only operation, table category, counts, duration, dry-run state, and safe failure category.
- Update BR-R05, BR-R06, AC-14, retention runbook, crawler/PageAudit policies, and traceability.

### 10.8 Exit gate

AC-14 passes: eligible raw data is removed while aggregates, active incidents, held data, current evidence, comparison baselines, and historical names remain correct.

## 11. Increment 7 — Representative hardening and release evidence

**Work item:** P7-MON-07  
**Priority:** Final personal-project evidence gate

This increment demonstrates behavior at representative portfolio scale. It does not certify a production deployment.

### 11.1 Workload

Use controlled local/CI targets only:

```text
500 endpoints
70 percent HTTPS
500 HTTP monitors
approximately 350 SSL monitors
up to 100 bounded concurrent checks across configured limits
```

Test distributed cadence and a 500-monitor due window. Include success, multi-address DNS, slow response, connection/HTTP/TLS failure, redirects, and bounded large bodies. Do not load third-party sites.

### 11.2 Zero-tolerance correctness gates

Required count is zero for duplicate terminal results, competing finalization, duplicate active incidents or monitors, prohibited-network escape, certificate-controlled networking, unknown-type networking, historical resurrection, cross-type mutation, stale current-state mutation, unauthorized target connection, stranded recoverable work, and retention deletion of active/current/held/baseline evidence.

### 11.3 Failure injection

Exercise controlled worker stops before/during/after requests and after finalization; enqueue failure; PostgreSQL outage; lease expiry; duplicate delivery; scheduler restart; DNS/address failure; TLS failure; outstanding-work restart; and retention interruption. Supported recovery cases require no manual database repair.

### 11.4 Representative performance targets

These are evidence goals for the recorded development machine and fixture, not universal SLAs:

```text
scheduler creation lag p95 <= 90 seconds
scheduler creation lag maximum <= 5 minutes
normal queue age p95 <= 2 minutes
normal queue age maximum <= 10 minutes
dashboard response p95 < 3 seconds
recovery <= configured recovery delay + 2 scheduler cycles
concurrency never exceeds configured global, host, or IP bounds
```

Record machine, PostgreSQL, .NET, workers, dataset, and fixtures. Record warm-up and two 15-minute memory windows and investigate sustained monotonic growth without claiming a universal guarantee.

### 11.5 Authorization, security, and delivery evidence

- Re-run direct authorization for every role and anti-forgery, encoding, CSV, diagnostics, and Hangfire tests.
- Confirm authorization for every controlled target.
- Confirm no secrets in repository, logs, audits, emails, diagnostics, or artifacts.
- Run unit, ordinary integration, JavaScript, and full ordered database-foundation suites.
- Record query plans, performance results, and changed-UI browser evidence.
- Update roadmap checkboxes only when linked evidence exists.
- Document known limitations and deferred deployment work.

### 11.6 Exit gate

The result may be described as a completed personal internship/portfolio implementation with representative monitoring-hardening evidence when correctness, security, AC-14, and AC-15 pass and limitations are documented. Do not make production, enterprise, certification, HA, or independent-audit claims.

## 12. Implementation order

```text
P7-MON-01 immediate correctness
    -> P7-MON-02 snapshot v2 and generation
    -> P7-MON-03 complete HTTP policy
    -> P7-MON-04 SSL correctness and security
    -> P7-MON-05 operational diagnostics
    -> P7-DATA-01 project-wide retention and holds
    -> P7-MON-07 representative release evidence
```

Do not accept snapshot work without archive-restore and pause-resume races. Do not enable retention before dry-run evidence. Do not describe the final increment as production certification.

## 13. Global migration rules

Every migration change updates, in the same work item:

- EF configuration, model snapshot, and compiled model;
- `ExpectedMigrations`, `ExpectedTables`, and `TablesAddedAfterPhaseThree` where applicable;
- table, column, foreign-key, and constraint assertions;
- hand-written negative-test INSERTs affected by new required columns;
- clean creation, previous-version upgrade, repeatability, and `Down` evidence.

Never migrate the shared populated database backward during the ordered database-foundation suite. Upgrade and repeatability checks use isolated databases.

## 14. Global completion rule

Each work item is complete only when all applicable parts are present:

- linked BR and AC identifiers;
- user-visible behavior and server-side authorization;
- inputs, outputs, validation, and error behavior;
- data and migration impact;
- security and privacy analysis;
- unit and integration tests;
- structured logging and operational signals;
- documentation and traceability updates;
- local/demo compatibility notes;
- self-review and linked test evidence.

The final result is a secure, explainable, and demonstrable monitoring implementation for a personal internship project. Any later real deployment requires a separate decision and additional hosting, backup, restoration, secret-management, external-observability, and rollout work.

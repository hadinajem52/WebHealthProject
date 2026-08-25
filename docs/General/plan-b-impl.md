
# Phase 7 — HTTP and SSL Monitoring Hardening Plan

**Status:** Ready for implementation
**Primary objective:** Make HTTP and SSL monitoring deterministic, race-safe, security-safe, resilient, and highly resistant to false incidents and false recoveries.

---

# 1. Purpose

This phase hardens the existing WebHealth HTTP and SSL monitoring pipeline.

The primary correctness rule is:

> **A failure inside WebHealth must never automatically be treated as proof that the monitored target failed.**

Monitoring must distinguish:

```text
target is healthy

target was conclusively observed failing

WebHealth could not obtain trustworthy evidence
```

The system therefore treats monitoring as an evidence pipeline rather than simply:

```text
request exception
    ->
website down
```

The implementation must prevent:

```text
monitoring infrastructure failures
scheduler failures
worker cancellation
configuration failures
authorization failures
stale work
out-of-order work
maintenance windows
execution retries
manual checks
ambiguous body validation
```

from incorrectly creating, escalating, recovering, or resolving target incidents.

This phase cannot guarantee that a single monitoring vantage always reflects global Internet availability.

A target incident therefore means:

> **The target was conclusively unhealthy according to WebHealth's configured monitoring policy from the WebHealth monitoring vantage.**

---

# 2. Non-Negotiable Monitoring Invariants

These are implementation contracts.

## 2.1 One logical check equals one observation

Retries do not create additional health evidence.

```text
LogicalCheck
    -> zero or more ExecutionAttempts
    -> exactly one terminal observation
```

Therefore:

```text
HTTP transport retry
Hangfire retry
lease recovery
duplicate job delivery
worker restart
```

must never increase confirmation counters independently.

---

## 2.2 Target failure and monitoring failure are different

Every completed check receives:

```text
ObservationVerdict

Healthy
TargetFailure
Inconclusive
```

Only:

```text
ObservationVerdict = Healthy
```

may provide recovery evidence.

Only:

```text
ObservationVerdict = TargetFailure
```

may provide failure evidence.

`Inconclusive` provides neither.

---

## 2.3 Current-state application is separate from observation verdict

Introduce:

```text
CurrentStateDisposition

Applied
Superseded
OutOfOrder
Ineligible
Suppressed
```

Examples:

```text
Healthy + Applied

TargetFailure + Applied

Inconclusive + Applied

TargetFailure + Superseded

Healthy + OutOfOrder

TargetFailure + Suppressed
```

Observation verdict answers:

> What did this execution observe?

Disposition answers:

> Is this observation allowed to influence current monitoring state?

---

## 2.4 Only state-eligible observations mutate automated state

An observation may modify automated health, confirmation counters, incidents, and notifications only when all apply:

```text
supported snapshot version
same active monitor identity
same CurrentTruthGeneration
same ConfigurationFingerprint
state-eligible source
current lifecycle eligible
current authorization valid
not maintenance-suppressed
not out-of-order
```

---

## 2.5 Historical observations are preserved

A stale, superseded, suppressed, or inconclusive check remains visible historically.

It simply cannot corrupt current truth.

---

# 3. Observation Classification

Create a single centralized classifier.

```text
IMonitoringObservationClassifier
MonitoringObservationClassifier
```

Do not distribute classification logic across controllers, jobs, incident services, and transports.

---

## 3.1 HTTP classification

### Healthy

Examples:

```text
accepted final HTTP response
required marker positively found
latency within configured threshold
```

### TargetFailure

Examples:

```text
unaccepted final HTTP response

connection actively refused

overall target timeout
when no local monitoring infrastructure
failure has been identified

TLS certificate validation prevents
normal HTTPS use

complete successfully decoded body
definitively lacks required marker
```

### Inconclusive

Examples:

```text
caller cancellation
worker shutdown
application cancellation
local network unavailable
local socket/resource exhaustion
DNS transient failure
DNS resolver timeout
internal monitor exception
configuration drift
unsupported response decoding
when marker absence cannot be proven
truncated body where marker was not found
```

---

## 3.2 Policy and authorization failures

These are not target failures.

Examples:

```text
destination prohibited
authorization revoked
authorization expired
unsupported scheme
unknown monitor type
invalid monitor configuration
unsafe DNS result
redirect destination unauthorized
```

Use:

```text
ObservationVerdict = Inconclusive
CurrentStateDisposition = Ineligible
```

They must produce:

```text
0 incident evidence
0 failure confirmations
0 recovery confirmations
0 target notifications
```

---

## 3.3 DNS classification

DNS errors must be separated into at least:

```text
DnsNameNotFound
DnsTransient
DnsResolverFailure
```

`SERVFAIL`, resolver timeout, local resolver problems, cancellation, and similar failures are:

```text
Inconclusive
```

A definitive name-not-found result may be considered target failure only when the implementation can reliably distinguish it from transient resolver failure.

If that distinction cannot be made reliably:

```text
fail toward Inconclusive
```

rather than manufacturing target failure.

---

# 4. Source Eligibility

Introduce a single definition:

```text
StateEligibleSource
```

Recommended behavior:

| Source                      |          History | Current automated state | Confirmation counters |
| --------------------------- | ---------------: | ----------------------: | --------------------: |
| Scheduled                   |              Yes |                     Yes |                   Yes |
| Urgent SSL                  |              Yes |                     Yes |                   Yes |
| Manual                      |              Yes |                      No |                    No |
| Retry of same logical check | Same observation |    No additional effect |  No additional effect |

Manual checks expose:

```text
LastManualObservation
```

separately.

They do not accidentally open or recover automated incidents.

---

# 5. Snapshot v2 and Immutable Execution

Preserve the existing snapshot-v2 architecture. 

Introduce:

```text
ResolvedMonitoringTarget

EndpointId
NormalizedUrl
NormalizedHost
EffectivePort
NormalizationVersion
IsProduction
```

HTTP:

```text
ResolvedHttpCheckConfiguration

Target
Policy
ConfigurationFingerprint
CurrentTruthGeneration
StateSequence
```

SSL:

```text
ResolvedSslCheckConfiguration

Target
Policy
ConfigurationFingerprint
CurrentTruthGeneration
StateSequence
```

Snapshot v2 contains:

```text
target_normalized_url
target_normalized_host
target_effective_port
target_normalization_version
target_is_production

current_truth_generation

state_sequence
```

Execution must use the snapshot for:

```text
target
port
policy
fingerprint
generation
```

It must not reread current endpoint configuration to determine what historical work means.

---

# 6. Current Authorization Remains Live

Authorization is never snapshotted.

Immediately before every outbound connection:

```text
snapshotted endpoint
snapshotted host
snapshotted port
+
current TargetAuthorizationEvidence
```

must authorize the connection.

Redirects independently repeat authorization.

If authorization is no longer valid:

```text
0 outbound connections
```

and:

```text
ObservationVerdict = Inconclusive
CurrentStateDisposition = Ineligible
```

Never place authorization evidence in:

```text
snapshot
logs
metrics
audit text
diagnostics
```

---

# 7. CurrentTruthGeneration

Keep:

```text
EndpointMonitor.CurrentTruthGeneration
```

Generation changes when queued observations can become semantically stale.

Examples:

```text
URL change
host change
port change
normalization change
production classification change

effective policy change
fingerprint change

pause
resume
enable
disable

archive
restore
retire
replace

HTTP <-> HTTPS

parent lifecycle eligibility change
```

Do not increment generation for:

```text
NextDueAt
dispatch timestamps
execution bookkeeping
```

Generation increments must be atomic SQL operations inside the same mutation transaction.

Never:

```text
read
increment in memory
save
```

---

# 8. Monotonic Observation Ordering

Generation fencing alone is insufficient.

Add:

```text
EndpointMonitor.LastIssuedStateSequence
EndpointMonitor.LastCompletedStateSequence
```

State-eligible logical-check creation atomically allocates:

```text
StateSequence =
    LastIssuedStateSequence + 1
```

using a database atomic operation.

Manual checks do not consume automated state sequence numbers.

---

## 8.1 Completion rule

Before automated state mutation:

```text
snapshot.StateSequence
    >
monitor.LastCompletedStateSequence
```

must hold.

Otherwise:

```text
CurrentStateDisposition = OutOfOrder
```

and the result remains historical only.

---

## 8.2 Newer inconclusive results still fence older work

Example:

```text
#100 starts

#101 starts
#101 completes Inconclusive

#100 later completes TargetFailure
```

`#100` must not become current.

Therefore a valid current-generation completion advances:

```text
LastCompletedStateSequence
```

even when its verdict is:

```text
Inconclusive
```

This guarantees that observations cannot travel backward in time.

---

# 9. Finalization Transaction

Current-state finalization must occur under one transaction/concurrency boundary.

Validate:

```text
same monitor identity

same generation

same fingerprint

current lifecycle eligibility

current authorization eligibility

state sequence is newest

maintenance status

state-eligible source
```

Then:

```text
persist terminal CheckResult
persist immutable observation/findings

advance LastCompletedStateSequence

apply confirmation state if appropriate

update confirmed health if appropriate

update incident state if appropriate

persist notification intent if appropriate
```

The transaction must prevent two workers from successfully applying competing finalizations.

---

# 10. Confirmation State Machine

Create one explicit confirmation service:

```text
IMonitoringConfirmationService
MonitoringConfirmationService
```

Never maintain confirmation counters independently inside HTTP, SSL, incidents, or workers.

Confirmation state is tracked per:

```text
MonitorId
CurrentTruthGeneration
IssueKey
```

Examples:

```text
Http.Availability
Http.Latency
Http.Content

Ssl.Expiry
Ssl.HostnameMismatch
Ssl.Untrusted
Ssl.NotYetValid
```

---

## 10.1 Failure streak

A failure confirmation streak requires consecutive:

```text
Applied
+
TargetFailure
+
state-eligible
+
same generation
+
same IssueKey
```

observations.

Example:

```text
Failure
Failure
```

with threshold `2`:

```text
confirmed failure
```

---

## 10.2 Inconclusive breaks the streak

```text
Failure
Inconclusive
Failure
```

does not equal two confirmations.

Reset:

```text
FailureConfirmationCount = 0
RecoveryConfirmationCount = 0
```

for that issue.

The existing confirmed health is not automatically replaced.

---

## 10.3 Healthy recovery

An active confirmed issue requires consecutive:

```text
Applied Healthy observations
```

where that particular issue is absent.

Example with recovery threshold `2`:

```text
Healthy
Healthy
    ->
issue recovered
```

---

## 10.4 Other failures do not count as recovery

If:

```text
Ssl.Expiry recovered
```

but:

```text
Ssl.HostnameMismatch remains
```

only the expiry issue may recover.

Overall endpoint health remains derived from remaining confirmed issues.

---

## 10.5 Retries never count twice

```text
LogicalCheck #123
Attempt 1 -> failure
Attempt 2 -> failure
Attempt 3 -> terminal failure
```

equals:

```text
one failure observation
```

---

## 10.6 Confirmation-window bound

Confirmation samples must not span an unlimited time.

Define:

```text
ConfirmationWindow =
    max(
        IntervalSeconds * ConfirmationCount * 2,
        15 minutes
    )
```

If the previous eligible confirmation evidence is older than the window:

```text
reset streak
```

This prevents two isolated failures hours apart from opening one incident.

---

# 11. Maintenance Suppression

Maintenance becomes part of finalization eligibility.

Before confirmation mutation:

```text
IsTargetUnderMaintenance(
    EndpointId,
    ObservationTime
)
```

must be evaluated through the existing maintenance policy.

During maintenance:

```text
persist observation
CurrentStateDisposition = Suppressed

do not advance failure confirmation
do not advance recovery confirmation
do not open incident
do not escalate incident
do not resolve incident
do not send target notification
```

---

## 11.1 Maintenance breaks confirmation streaks

When entering or leaving a maintenance period:

```text
reset unconfirmed failure/recovery streaks
```

After maintenance ends, monitoring requires fresh evidence.

---

## 11.2 Existing incidents

Maintenance does not fabricate recovery.

An incident open before maintenance remains historically open unless genuine recovery is confirmed afterward.

Notifications may be muted according to maintenance policy.

---

# 12. Scheduling and Overlap Control

There must be at most:

```text
1 non-terminal state-eligible scheduled
LogicalCheck
per MonitorId + CurrentTruthGeneration
```

Enforce this using transaction-safe creation and, where practical, a database constraint/index.

---

## 12.1 Slow monitor behavior

If a 5-minute monitor takes 8 minutes:

```text
do not create overlapping scheduled checks
```

The next scheduler pass observes the existing active check and does not create another.

---

## 12.2 Scheduler catch-up

Missed cadence slots are coalesced.

Wrong:

```text
10:00 missed
10:05 missed
10:10 missed
10:15 missed

restart

create 4 checks
```

Correct:

```text
restart
    ->
create at most one current due check
    ->
advance NextDueAt to next future cadence slot
```

Missed executions are diagnostic information, not retroactive health evidence.

---

## 12.3 Duplicate scheduler execution

Multiple scheduler workers must not create duplicate checks for the same due window.

Use:

```text
database concurrency
unique invariant
or atomic due-claim
```

not process-local locking alone.

---

# 13. Safe Destination Connector

Preserve the existing SSRF-safe design. 

Algorithm:

```text
resolve DNS
    ↓
enforce raw-answer bound
    ↓
normalize IPv4-mapped IPv6
    ↓
deduplicate preserving resolver order
    ↓
validate every answer
    ↓
any prohibited answer?
    -> reject entire destination

otherwise

try addresses sequentially
    ↓
per-IP concurrency lease
    ↓
fresh socket
    ↓
bounded per-address timeout
    ↓
verify actual peer
    ↓
revalidate connected peer
```

---

## 13.1 Timeouts

```text
PerAddressConnectTimeout
Default = 5 sec
Min = 1 sec
Max = 10 sec
```

Overall check timeout remains the absolute deadline.

Per-address timeout may advance to another public candidate.

Overall cancellation stops further candidates.

---

# 14. HTTP Transport Hardening

The monitoring transport must be deterministic and stateless.

Configure intentionally:

```text
AllowAutoRedirect = false
UseCookies = false
UseDefaultCredentials = false
UseProxy = false
```

unless proxy support becomes an explicitly designed monitoring feature.

Do not silently inherit machine/environment proxy configuration.

---

## 14.1 Redirect handling

Redirects are processed by WebHealth.

Every hop repeats:

```text
scheme validation
authorization
DNS policy
address validation
peer validation
```

Only:

```text
http
https
```

are supported.

Default:

```text
HTTPS -> HTTP downgrade = rejected
```

unless a future explicit bounded policy enables it.

---

## 14.2 Redirect loops

Detect:

```text
hop count
repeated canonical destination
invalid Location
missing Location where required
```

A redirect-policy failure is not misreported as a generic website outage.

---

# 15. HTTP Policy

Use typed overrides:

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

Internal application defaults remain:

```text
MaxResponseBodyBytes
MaxRedirects
ProductionHttpSeverity
AllowHttpsToHttpRedirect = false
```

All behavior-affecting settings belong in the resolved policy and fingerprint.

---

# 16. HTTP Defaults

```text
Production interval       5 minutes
Non-production interval   15 minutes

Timeout                   15 sec
Range                     1-120 sec

Failure confirmations     2
Range                     1-10

Recovery confirmations    2
Range                     1-10

Warning latency           1500 ms
Critical latency          3000 ms

Additional statuses       300-499
Distinct maximum          20

Required marker           optional
Maximum length            500 characters

Comparison                Ordinal / OrdinalIgnoreCase
```

All `2xx` remain accepted.

`5xx` can never be configured healthy.

---

# 17. HTTP Response-Time Semantics

Use monotonic elapsed timing.

Do not calculate latency using wall-clock timestamp subtraction.

Record separately:

```text
ResponseTimeMs
CheckDurationMs
```

Recommended contract:

```text
ResponseTimeMs =
    elapsed time until final response headers

CheckDurationMs =
    total execution including bounded body validation
```

Latency health thresholds operate on:

```text
ResponseTimeMs
```

This prevents a large validation body from being incorrectly interpreted as slow server response.

---

# 18. Required-Content Matching

The response buffer remains bounded.

Automatic decompression must be explicitly configured.

`MaxResponseBodyBytes` applies to:

```text
decompressed bytes actually inspected
```

not only compressed wire size.

---

## 18.1 Supported encodings

```text
UTF-8
US-ASCII
ISO-8859-1
```

Charset names are case-insensitive.

Use strict decoding for validation.

---

## 18.2 Positive match

If the required marker is found:

```text
content requirement satisfied
```

even if additional body data was truncated afterward.

---

## 18.3 Complete body mismatch

If:

```text
response body fully consumed
supported decoding successful
marker not found
```

then:

```text
TargetFailure
IssueKey = Http.Content
```

---

## 18.4 Truncated body

If:

```text
MaxResponseBodyBytes reached
marker not found
body has more data
```

then:

```text
Inconclusive
```

Never conclude:

```text
marker absent
```

when the unread portion could contain it.

---

## 18.5 Unsupported/malformed charset

Attempt safe fallback only for positive detection.

If fallback finds the marker:

```text
satisfied
```

If fallback does not find the marker and absence cannot be proven:

```text
Inconclusive
```

not `TargetFailure`.

---

# 19. HTTP Fingerprint

Fingerprint covers:

```text
normalized URL
monitor type
production flag

interval
timeout

failure confirmations
recovery confirmations

warning latency
critical latency

canonical accepted statuses

required marker
comparison mode

MaxResponseBodyBytes
MaxRedirects
ProductionHttpSeverity
AllowHttpsToHttpRedirect
```

Canonical status representation:

```text
deduplicate
sort
serialize deterministically
```

Fingerprint drift:

```text
0 network requests
Ineligible
structured operational error
```

---

# 20. SSL Policy

Use:

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

Validation:

```text
Warning > High > Critical >= 0
```

---

# 21. SSL TLS Behavior

Both HTTP HTTPS transport and SSL probe must disable certificate-controlled networking.

```text
CertificateRevocationCheckMode = NoCheck
DisableCertificateDownloads = true
```

Therefore:

```text
CRL request = 0
OCSP request = 0
AIA download = 0
```

Revocation is reported as:

```text
Not checked
```

Never:

```text
Good
Revoked
```

unless actual revocation checking is later implemented.

This preserves the security contract already present in the original plan. 

---

# 22. SSL Connection Identity

The TLS SNI/target name must always use:

```text
snapshotted normalized hostname
```

Never:

```text
connected IP address
```

for hostname validation.

Do not write custom SAN/wildcard matching logic.

Use supported .NET/platform hostname validation.

---

# 23. SSL Structured Facts

Persist:

```text
ValidityStatus
    Valid
    Expired
    NotYetValid

HostnameStatus
    Matched
    Mismatched

ChainTrustStatus
    Trusted
    Untrusted
    Unknown

ChainStatusCodes
```

`Unknown` must not automatically produce:

```text
Ssl.Untrusted
```

Unknown means:

```text
insufficient trustworthy evidence
```

unless a specific untrusted status is available.

---

# 24. SSL Expiry Calculation

Use exact UTC instants.

Example:

```text
NotAfter <= observationTime + 30 days
```

not:

```text
floor(DaysRemaining) <= 30
```

`DaysRemaining` is presentation data.

It does not determine severity.

---

# 25. SSL Trust Model

Document explicitly:

```text
ChainTrustStatus reflects the trust store
of the WebHealth monitoring environment.
```

Unless custom CA configuration is intentionally added, private/self-signed/internal PKI certificates may therefore be reported untrusted.

This is expected policy behavior, not a monitor bug.

---

# 26. Multiple SSL Findings

Evaluate independently:

```text
Ssl.Expiry
Ssl.HostnameMismatch
Ssl.Untrusted
Ssl.NotYetValid
```

One certificate can generate several findings.

Do not suppress a real condition only because another condition is more severe.

Primary display precedence may remain:

```text
NotYetValid
Expired
HostnameMismatch
Untrusted
ExpiringSoon
```

but that display category does not replace underlying findings.

---

# 27. Confirmed Health vs Monitoring State

Keep these separate.

Confirmed target health:

```text
Healthy
Warning
Critical
Unknown
```

Monitoring operational state:

```text
Active
ManualOnly
Paused
Disabled
Delayed
Stale
NeverChecked
```

Never represent:

```text
monitor disabled
```

as target health failure.

The original plan already makes this distinction; retain it. 

---

# 28. Freshness Semantics

Track separately:

```text
LastScheduledExecutionCompletedAt

LastCurrentConclusiveScheduledObservationAt
```

The first proves:

```text
scheduler/executor is running
```

The second proves:

```text
WebHealth recently obtained trustworthy target evidence
```

---

## 28.1 Target freshness

`Stale` and `NeverChecked` use:

```text
LastCurrentConclusiveScheduledObservationAt
```

not merely any completed scheduled job.

Therefore these do not refresh target freshness:

```text
Superseded
OutOfOrder
Ineligible
Suppressed
Inconclusive
Manual
```

---

# 29. Monitoring Runtime Health

Track:

```text
monitoring-dispatch
monitoring-reconciliation
```

with:

```text
last_started_at
last_succeeded_at
last_failed_at
last_duration_ms
last_failure_category
consecutive_failures
```

Every invocation updates the heartbeat even with zero work.

---

# 30. Monitoring Infrastructure Failures

Add explicit infrastructure categories:

```text
LocalNetworkUnavailable
DnsResolverUnavailable
SocketResourceExhaustion
WorkerCancellation
DatabaseUnavailable
SchedulerUnavailable
QueueUnavailable
InternalMonitorFailure
```

These degrade monitoring-engine health.

They do not create target incidents.

---

# 31. Monitoring Health Thresholds

Retain:

```text
scheduler warning     3 min
scheduler critical    5 min

worker tolerance      2 min

queue warning         5 min
queue critical        15 min

overdue warning       DispatchDelayGrace
overdue critical      30 min

critical consecutive scheduler failures = 3
```

Also include:

```text
recent local-network infrastructure failures
recent DNS resolver failures
```

in monitoring-health evaluation.

A monitored target being down does not make application readiness unhealthy.

A monitoring-engine failure does not make 500 targets appear down.

---

# 32. Notification Idempotency

Incident correctness is incomplete if workers can send duplicate notifications.

Every target notification must have a durable idempotency identity.

For example:

```text
IncidentId
IncidentEventId
NotificationKind
Channel
```

with an appropriate unique invariant.

Retries reuse the same notification intent.

Worker crashes may produce:

```text
retry
```

but not:

```text
duplicate user-visible notification
```

---

# 33. Incident Transition Rules

For each issue:

```text
unconfirmed
    ↓ enough failure observations
confirmed/open
    ↓ enough recovery observations
resolved
```

Severity escalation requires confirmed evidence.

A single transient latency spike must not bypass confirmation merely because its severity is high.

Existing open incidents are never resolved by:

```text
Inconclusive
Suppressed
Ineligible
Superseded
OutOfOrder
```

---

# 34. Monitor Reconciliation

Retain the existing reconciliation design from the supplied plan. 

Create:

```text
EndpointMonitorReconciler
```

Use for:

```text
archive
restore
HTTP -> HTTPS
HTTPS -> HTTP
host change
port change
```

Preserve historical monitors.

Never resurrect arbitrary historical monitors.

HTTP:

```text
maximum one active compatible HTTP monitor
```

HTTPS:

```text
maximum one active compatible SSL monitor
```

HTTP endpoint:

```text
active SSL monitors = 0
```

PageAudit remains owned by its own configuration path.

---

# 35. Database Changes

At minimum:

## EndpointMonitor

```text
CurrentTruthGeneration

LastIssuedStateSequence
LastCompletedStateSequence
```

## CheckConfigurationSnapshot

```text
schema_version

target_normalized_url
target_normalized_host
target_effective_port
target_normalization_version
target_is_production

current_truth_generation
state_sequence
```

## CheckResult

```text
observation_verdict
current_state_disposition
failure_category
```

If needed for diagnostics:

```text
response_body_complete
```

without persisting body contents.

## Confirmation state

Introduce or adapt a durable structure representing:

```text
MonitorId
Generation
IssueKey

ConsecutiveFailureCount
ConsecutiveRecoveryCount

LastEvidenceSequence
LastEvidenceAt
```

---

# 36. Snapshot Upgrade Rule

After snapshot-v2 deployment:

```text
only v2 state-eligible checks
may mutate automated current truth
```

Existing v1 work:

```text
may finish
may persist history

must not mutate health
must not mutate counters
must not mutate incidents
must not notify
```

Use:

```text
CurrentStateDisposition = Superseded
```

---

# 37. Migration Requirements

Every schema change updates:

```text
EF configuration
model snapshot
compiled model

ExpectedMigrations
ExpectedTables

column assertions
FK assertions
constraint assertions

negative INSERT tests

clean creation
upgrade tests
repeatability tests
Down evidence
```

Database migration tests use isolated databases.

Timestamp assertions respect PostgreSQL microsecond precision.

---

# 38. P7-MON-00 — Monitoring Truth Model

**Priority:** P0
**Must be implemented first.**

Implement:

```text
ObservationVerdict
CurrentStateDisposition
state-source eligibility
state sequence
ordering fence
confirmation state machine
maintenance suppression
notification idempotency contract
```

Required tests:

```text
older Failure finishes after newer Healthy

older Healthy finishes after newer Failure

newer Inconclusive fences older Failure

same sequence cannot finalize twice

two workers compete to finalize same check

execution retry counts once

duplicate Hangfire delivery counts once

manual check cannot open incident

manual check cannot resolve incident

Inconclusive breaks failure streak

Inconclusive breaks recovery streak

maintenance suppresses failure

maintenance suppresses recovery

maintenance breaks existing streak

confirmation window expiry resets streak

notification retry sends once
```

### Exit gate

No automated target state may change without passing the common truth model.

---

# 39. P7-MON-01 — Safe Transport and Reconciliation

Implement:

```text
multi-address DNS fallback
raw-answer bound
address validation
peer validation
per-address timeout
connection fallback
transport hardening
redirect authorization
monitor reconciliation
type-safe monitor execution
```

Tests include:

```text
first IP fails -> second succeeds
IPv6 fails -> IPv4 succeeds
mixed public/private answers reject
raw MaxDnsAnswers
connected-peer mismatch
per-IP lease release
HTTP -> HTTPS
HTTPS -> HTTP
archive -> restore
host change
port change
historical monitor preservation
unknown monitor type -> zero network access
```

---

# 40. P7-MON-02 — Scheduling Integrity

Implement:

```text
one non-terminal scheduled check per monitor generation

atomic due claiming

missed-slot coalescing

no overlapping scheduled execution

no retrospective health backfill
```

Tests:

```text
slow check crosses next interval

scheduler runs concurrently twice

scheduler down for 30 minutes

restart creates one check, not N checks

duplicate due dispatch

worker crash + retry

generation changes while check active
```

---

# 41. P7-MON-03 — Complete HTTP Monitoring

Implement:

```text
typed policy resolver
policy bounds
fingerprint
status evaluation
manual redirects
monotonic latency
bounded decompressed response validation
strict marker semantics
failure classification
```

Required tests:

```text
all configuration bounds

2xx implicit acceptance
configured 401/403
5xx rejected as healthy

redirect loop
unauthorized redirect
HTTPS downgrade

UTF-8
ASCII
Latin-1
invalid encoding
unsupported encoding

marker found before truncation
marker absent complete body
marker absent truncated body

gzip/brotli bounded validation

timeout
connection refused
transient DNS
local cancellation

legacy overrides
canonical migration
fingerprint stability

manual/scheduled policy equivalence
```

---

# 42. P7-MON-04 — Complete SSL Monitoring

Implement:

```text
resolved SSL policy
SSL fingerprint
SNI correctness
platform hostname validation
chain trust
leaf validity
exact expiry thresholds
multiple findings
certificate network isolation
```

Required tests:

```text
valid certificate
expired leaf
not-yet-valid leaf
hostname mismatch
untrusted root
partial chain
expired intermediate

mismatch + untrusted
expiry + another finding

exact 30-day boundary
exact 15-day boundary
exact 7-day boundary

SNI uses hostname
connected IP is not hostname input

Unknown chain state
canonical chain statuses

certificate renewal
urgent SSL check

AIA -> zero request
CRL -> zero request
OCSP -> zero request

normal HTTPS rejects invalid cert
SSL probe never sends application data
```

---

# 43. P7-MON-05 — Operational Diagnostics

Implement:

```text
confirmed health
operational state

execution freshness
conclusive observation freshness

scheduler heartbeat
reconciliation heartbeat

worker health
queue health
overdue monitor health

local monitoring-infrastructure failures

structured logs
low-cardinality metrics
```

Required diagnostics:

```text
ScheduledMonitorCount
DisabledMonitorCount
PausedMonitorCount
ManualOnlyMonitorCount
DelayedMonitorCount
StaleMonitorCount
NeverCheckedMonitorCount

PendingWorkCount
DispatchingWorkCount
EnqueuedWorkCount
ProcessingWorkCount
FailedWorkCount
RecoverableWorkCount

OldestOverdueMonitorAge
OldestQueuedWorkAge

LastScheduledExecutionCompletedAt
LastConclusiveScheduledObservationAt
```

---

# 44. P7-MON-06 — False-Positive Certification

Run this before considering HTTP/SSL monitoring behavior complete.

## Required deterministic scenarios

```text
single transient DNS resolver failure
    -> no target incident

worker cancellation
    -> no target incident

authorization revoked
    -> no target incident

maintenance outage
    -> no target incident

truncated content without marker
    -> no content incident

manual failure repeated twice
    -> no automatic incident

same logical check retried 5 times
    -> one observation

two scheduled failures
    -> one confirmed issue

healthy + inconclusive + healthy
    -> no false recovery

failure + inconclusive + failure
    -> no false opening

old failure arrives after new healthy
    -> old failure historical only

old healthy arrives after new failure
    -> old healthy historical only

scheduler catches up after outage
    -> no confirmation burst

certificate probe cannot contact AIA/CRL/OCSP
```

---

# 45. Zero-Tolerance Gates

Required count:

| Failure                                            | Allowed |
| -------------------------------------------------- | ------: |
| Duplicate terminal result                          |       0 |
| Competing successful finalization                  |       0 |
| Duplicate active incident                          |       0 |
| Duplicate notification transition                  |       0 |
| Duplicate active monitor/type                      |       0 |
| Prohibited-network escape                          |       0 |
| Certificate-controlled outbound request            |       0 |
| Unknown-type outbound request                      |       0 |
| Historical monitor resurrection                    |       0 |
| Stale generation mutating current state            |       0 |
| Out-of-order result mutating current state         |       0 |
| Retry counted as separate confirmation             |       0 |
| Manual check counted as automated confirmation     |       0 |
| Maintenance-suppressed incident transition         |       0 |
| Inconclusive observation treated as target failure |       0 |
| Truncated-body false content failure               |       0 |
| Scheduler catch-up confirmation burst              |       0 |
| Unauthorized snapshotted-target connection         |       0 |
| Permanently stranded recoverable work              |       0 |

Any failure blocks monitor completion.

---

# 46. Representative Failure Injection

Exercise:

```text
worker dies before request
worker dies during request
worker dies after response
worker dies during finalization
worker dies after finalization

duplicate job delivery

Hangfire enqueue failure

database temporary outage

lease expiry

scheduler stop
scheduler restart

DNS temporary failure
DNS name failure
first IP failure
all candidate IP failure

local network failure

TLS handshake failure

application restart with outstanding work
```

Supported recovery scenarios must require:

```text
no manual DB repair
```

---

# 47. Representative Scale Test

Retain the existing representative profile: 

```text
500 endpoints

500 HTTP monitors
approximately 350 SSL monitors

up to 100 concurrent checks
within configured concurrency limits
```

Include:

```text
healthy targets
slow targets
connection failures
DNS fallback
HTTP failures
TLS failures
redirects
large bounded responses
content-marker cases
```

Do not load-test third-party public websites.

---

# 48. Core Monitoring Completion Gate

Before working on retention as a prerequisite, HTTP/SSL monitoring is behaviorally ready when:

```text
P7-MON-00 passes
P7-MON-01 passes
P7-MON-02 passes
P7-MON-03 passes
P7-MON-04 passes
P7-MON-05 passes
P7-MON-06 passes
```

At that point you can reasonably claim:

> **The HTTP and SSL monitoring engine has been hardened against stale state, out-of-order execution, retry amplification, ambiguous evidence, maintenance suppression errors, scheduler overlap, infrastructure-induced false incidents, and false recovery.**

---

# 49. Data Lifecycle Work

The existing Phase 7 data work remains valid but should no longer block proving the monitoring engine itself is correct.

Implement after the core monitoring gate:

```text
P7-DATA-01A
Daily aggregates + long-window reporting

P7-DATA-01B
Retention holds

P7-DATA-01C
Project-wide bounded retention
```

Keep the original guarantees:

```text
90-day raw monitoring data

24-month aggregates

24-month certificate observations

24-month terminal incident bundles

active incidents never age-deleted

held data never age-deleted while held

aggregate-before-delete

reference-aware logical-check retention

bounded active-incident evidence

dry-run before destructive retention

transaction-local retention permission
```

Those are already well specified in your original plan. 

---

# 50. Final Implementation Order

```text
P7-MON-00
Truth model
observation verdicts
ordering
confirmation semantics
maintenance
notification idempotency
        ↓

P7-MON-01
Safe transport
DNS fallback
reconciliation
        ↓

P7-MON-02
Scheduling integrity
overlap prevention
catch-up coalescing
        ↓

P7-MON-03
HTTP policy + evidence correctness
        ↓

P7-MON-04
SSL correctness + security
        ↓

P7-MON-05
Operational monitoring health
        ↓

P7-MON-06
False-positive / race / failure certification
        ↓

CORE MONITORING READY
        ↓

P7-DATA-01A
Aggregates
        ↓

P7-DATA-01B
Retention holds
        ↓

P7-DATA-01C
Bounded retention
        ↓

FINAL PHASE 7 EVIDENCE
```

---

# 51. Final Definition of Done

## Evidence semantics

```text
✓ target failures and monitor failures are distinct

✓ inconclusive observations cannot create incidents

✓ manual observations cannot manipulate automated incidents

✓ retries cannot manipulate confirmation counts
```

## Ordering

```text
✓ generation fencing prevents old configuration from becoming current

✓ state sequencing prevents older same-generation results becoming current

✓ competing workers cannot both finalize current state
```

## Confirmation

```text
✓ failure confirmations require consecutive trustworthy evidence

✓ recovery confirmations require consecutive trustworthy evidence

✓ inconclusive evidence breaks streaks

✓ stale confirmation samples expire

✓ each issue confirms independently
```

## Maintenance

```text
✓ maintenance suppresses incident mutations

✓ maintenance breaks pending confirmation streaks

✓ maintenance does not fabricate recovery
```

## Scheduling

```text
✓ no overlapping scheduled check per generation

✓ missed schedules are coalesced

✓ scheduler restart cannot create confirmation bursts

✓ duplicate scheduler execution cannot duplicate logical checks
```

## HTTP

```text
✓ all destinations authorized

✓ every DNS answer validated

✓ public-address fallback works

✓ peer verified

✓ redirects independently authorized

✓ HTTPS downgrade rejected

✓ latency uses monotonic timing

✓ body validation is bounded

✓ compressed responses remain bounded

✓ truncated body cannot create false content failure

✓ ambiguous decoding cannot create false content failure
```

## SSL

```text
✓ SNI uses hostname

✓ platform hostname validation used

✓ leaf validity explicit

✓ chain trust explicit

✓ Unknown != Untrusted

✓ exact expiry boundaries used

✓ simultaneous faults retained

✓ certificate-controlled networking = 0

✓ normal HTTPS never accepts invalid certificates

✓ revocation accurately reported as not checked
```

## Operational health

```text
✓ confirmed health and monitoring health are separate

✓ scheduler health visible

✓ worker health visible

✓ queue health visible

✓ conclusive observation freshness visible

✓ local monitor infrastructure errors visible

✓ monitoring-engine failure cannot masquerade as mass target failure
```

## Reliability

```text
✓ duplicate job delivery safe

✓ worker restart safe

✓ stale execution safe

✓ out-of-order execution safe

✓ notification retries idempotent

✓ supported failures recover without manual DB repair
```

## Zero-tolerance

```text
✓ 0 stale current mutations
✓ 0 out-of-order current mutations
✓ 0 retry-amplified confirmations
✓ 0 maintenance false incidents
✓ 0 truncated-content false incidents
✓ 0 unauthorized connections
✓ 0 certificate-controlled requests
✓ 0 duplicate active incidents
✓ 0 duplicate transition notifications
```

---

## Final outcome

With these changes, **I would consider the plan implementation-ready for the monitoring goal you described**.

The biggest architectural improvement over the current version is this pipeline:

```text
network execution
        ↓
raw evidence
        ↓
ObservationVerdict
Healthy / TargetFailure / Inconclusive
        ↓
eligibility + generation + ordering + maintenance
        ↓
confirmation state machine
        ↓
confirmed issue
        ↓
health / incident / notification
```

instead of letting transport failures flow too directly into target health.

That separation is what makes the feature resistant to false positives rather than merely resilient to worker/database races.

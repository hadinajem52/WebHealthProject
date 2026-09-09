# Retention policy and implementation record

Increment 6 is implemented and its AC-14 database and browser gates are recorded. Retention remains disabled by default for explicit commissioning.

The implementation follows section 10 of the monitoring plan: raw monitoring, execution, crawl
and PageAudit history defaults to 90 days; certificate observations, daily aggregates and terminal
incident bundles default to 24 months. Current evidence, active incidents, active holds and required
comparison baselines take precedence over age.

Audit events have no age-based deletion in this local/demo implementation. Keeping them indefinitely
ensures they are never shorter-lived than the retained history they explain. A future audit retention
limit requires an explicit policy change and reference analysis; it is not implied by raw retention.

Repeated confirmed failures without a transition or severity increase do not add incident evidence,
timeline entries or audit mutations. Raw check history continues to capture each check. Opening,
severity escalation, recovery start, recovery interruption, resolution and certificate replacement
continue to create evidence. This bounds evidence growth for a steady incident without losing its
material transitions. Historical evidence is preserved; this change does not remove existing rows.

The worker defaults to disabled and dry-run, with batches of 1,000, at most 20 batches per run,
a 30-second run budget and an hourly schedule. Holds and retention configuration are Administrator
operations. Dry-run evidence must be recorded before enabling deletion.

## Hold storage

Migration `20260908144110_RetentionHolds` adds `retention_hold`. The supported scope types are
Client, Website, Environment, Endpoint, Monitor, LogicalCheck, Incident, CrawlRun and PageAuditRun.
Reason text is required and limited to 500 characters. Expiry is optional and must follow creation;
release time and actor must be supplied together, with release no earlier than creation.

Scope and actor IDs are historical identifiers, without cascading foreign keys. The management
service must validate their existence and authorization before writing. The retention worker must
expand scopes through registry and history relationships; merely adding the table does not enable
hold enforcement. No secondary indexes are introduced without representative query-plan evidence.

Rollback removes the holds table and its history. Do not roll back a deployment that relies on
holds while retaining an enabled deletion worker. Fresh upgrade creates no artificial holds.

## Hold management service

The application service limits listing to 100 records per page, ordered by creation time and ID.
Only an active authenticated Administrator can list, create or release holds. Archived scope
records remain eligible because historical data may still need protection. The service validates
scope existence; unknown scope types and missing records are rejected.

Creation and release share transaction advisory lock `(761924, 1)`. Every retention deletion batch
must acquire the same lock before evaluating holds. Release is idempotent: concurrent or repeated
requests preserve the first release actor/time and produce one release audit. Audit snapshots
contain scope and lifecycle identifiers, excluding the free-text reason. Reasons remain in the
protected hold record. Expiry is evaluated at PostgreSQL microsecond precision. The web management flow and every worker category enforce the same hold expansion.

## Administrator web flow

Administrators can open /Retention from monitoring diagnostics, create a hold using its scope
and record identifier, and release an active hold. The history shows active, expired and released
states in pages of 100 records. Expiry input is UTC. POST actions require antiforgery tokens and
server-side Administrator authorization; reason text is HTML-encoded. Desktop and 390-pixel browser checks confirm the dashboard shell, labeled controls, validation-summary behavior and absence of horizontal overflow. The form accepts record IDs from detail-page addresses or exports rather than offering a searchable record picker.

## Hold scope expansion

RetentionHoldQueries resolves active holds at one batch timestamp through existing relationships.
Client, website, environment and endpoint holds protect descendant monitor, check, incident, crawl
and PageAudit records. Monitor holds protect their checks and incident evidence. A held incident
protects the checks and PageAudit runs referenced by its evidence. A held check or PageAudit run
also preserves its referencing incident bundle, whose evidence must remain intact. A crawl-run
hold protects that run and its children without protecting unrelated monitoring history.

Released holds do not match; expiry is exclusive at its exact timestamp. Archived configuration
is deliberately included. The deletion worker must use these queries inside the shared retention
transaction lock. Query integration is implemented ahead of the worker; deletion is still disabled.

## Daily response-time histogram contract

Daily aggregates will preserve the existing report duration sample set: uptime-eligible Healthy
or Warning results. Version 1 buckets have inclusive upper bounds of 0, 100, 250, 500, 1000, 2500,
5000, 10000, 30000, 60000 and 120000 milliseconds, followed by an overflow bucket. Stored counts
are non-cumulative. Approximate percentiles select the nearest-rank bucket and use its upper bound,
capped by the recorded maximum; overflow uses the recorded maximum. Empty samples return no value.
Completed aggregate days retain compact exact duration samples while raw detail exists, so retained windows keep exact percentiles without scanning raw rows. Raw deletion clears those samples atomically; mixed or archived windows then disclose approximation rather than presenting bucket estimates as exact percentiles.

## Daily aggregate storage

Each monitor/UTC-day row stores total and scheduled counts, eligible Healthy/Warning/Down counts,
excluded/maintenance/cancelled counts, duration count/sum/minimum/maximum and the versioned histogram.
Maintenance and cancellation counts are subsets of excluded samples, not additional totals.
Comparability identity is a SHA-256 digest of the represented configuration identities; IsComparable
records whether they describe one comparable population. Source range and first/last measurement
retain report provenance. The writer populates these fields from raw results.

ComputedAt records recomputation. RawDeletionStartedAt is set before the first raw deletion for
that day; once set, recomputation from remaining raw rows is forbidden. Reports must choose the
complete aggregate for such days instead of double-counting protected raw rows that remain.
Endpoint purge removes its daily aggregates before monitors. The configured worker remains disabled by default.

## Aggregate recomputation

DailyAggregateWriter rebuilds one completed UTC day from raw result rows under the retention
transaction lock. It streams scalar result/snapshot fields, preserves existing uptime classification,
and includes only eligible Healthy/Warning duration samples. It supports an existing batch
transaction or owns one when called independently. Empty or current/future days are not written.

Configuration fingerprint, snapshot schema and truth generation form the comparability identity.
Distinct identities are hashed in deterministic database order without retaining a growing set in
memory. More than one identity marks the day non-comparable. Recomputing replaces counts rather
than incrementing them. Once RawDeletionStartedAt is present, recomputation returns without writing.
The deletion worker and historical report reader consume these contracts.

## Immutable history deletion permission

Migration 20260908151843_MonitoringRetentionPermission allows DELETE on check snapshots, incident
events and incident evidence when the current transaction sets web_health.monitoring_retention
to on. Existing endpoint-purge permission still works. UPDATE remains rejected even with either
permission. Maintenance-occurrence exemptions are unchanged. The worker must use SET LOCAL inside
each batch transaction; this change does not grant an application role permission or enable a job.

Down restores the endpoint-purge-only functions without rewriting stored history. Upgrade and
repeatability checks use an isolated database. Transaction completion must reset the retention
setting on the same connection, after both commit and rollback.

## Worker configuration bounds

Monitoring:Retention defaults are explicitly recorded in appsettings.json: Enabled=false,
DryRun=true, BatchSize=1000, MaximumBatchesPerRun=20 and MaximumRunDuration=00:00:30. Startup rejects
batch sizes outside 1..1000, batch counts outside 1..20 and durations outside 1..30 seconds. These
upper bounds keep the first implementation within the planned local/demo run budget. Increasing
them requires an explicit implementation/policy change. No job is registered by the options alone.

## Execution-attempt batch

The first deletion batch handles finished attempts older than 90 days whose logical checks are
also completed before that cutoff. It excludes holds, any retained execution lease, current health
evidence and active incident evidence. Candidates are ordered by finish time and ID and limited
to BatchSize. Dry-run selects the same candidates and deletes nothing. Each batch owns a transaction,
shares the retention lock, observes cancellation/runtime limits and logs only category/counts/time.
The hourly coordinator invokes this batch when retention is enabled.

## Completed durable-work batches

ExecutionHistoryRetentionBatch shares the transaction, deadline and protected-check query between
attempts and durable work. Durable work must be Completed, updated strictly before the 90-day cutoff,
and have no work lease fields; its logical check must also qualify. Pending, failed and leased work
remain. Both deletion paths reapply eligibility when deleting the selected IDs. The coordinator
invokes both paths in dependency order.

## Raw-result batches

RawResultRetentionBatch handles results measured strictly before the 90-day cutoff whose completed
checks pass the shared hold, lease, current-health and active-incident protections. Retained SEO or
certificate observations also preserve the result used by their readers. Observation expiration
and comparison-baseline protection are handled by the observation batches below.

Each batch selects at most BatchSize result IDs from one monitor and UTC day. Before its first
real deletion, it recomputes the full daily aggregate and records RawDeletionStartedAt in the same
transaction. Subsequent batches preserve that aggregate instead of recomputing from partial raw
history. Findings and redirect hops are removed before their selected results; logical checks and
snapshots remain. Dry-run does not write aggregates or deletion markers. The hourly coordinator
invokes this service; deletion remains disabled by default pending the acceptance gates.

## Observation expiration and current baselines

SEO observations follow the 90-day raw-detail policy. Certificate observations use 24 calendar
months (UTC AddMonths(-24)), not a fixed day count. Observation and completed-check timestamps
must both be strictly older than the category cutoff. The shared hold, lease, current-health and
active-incident protections apply before deletion.

Retain the latest observation timestamp per monitor and the latest Current result-backed
observation timestamp per monitor, including ties. This preserves current readers even when a
newer superseded observation exists or readers use different stable-ID tie directions. These
baseline exceptions take precedence over age. Observation cleanup removes only the observation;
the raw-result batch separately aggregates and removes eligible result detail afterward.

## Crawl retention and comparison baselines

Crawl runs expire strictly 90 days after FinishedAt. Running runs and active holds never expire.
Retain the latest terminal run per endpoint ordered by StartedAt then ID descending, and retain
the latest two comparison-eligible runs using the same ordering as CompareLatestAsync. Comparison
eligibility means Completed, FrontierExhausted, PagesFetched > 0 and no limited coverage. This
preserves the existing comparison even when the newest run failed or stopped at a limit. Archived
runs still participate because the report currently includes them. Terminal execution claim IDs
are historical fencing tokens, not live leases, and do not prevent expiration.

## PageAudit retention and comparison baselines

PageAudit runs expire strictly 90 days after FinishedAt. Queued/running runs, leased runs, active
holds and any run referenced by retained incident evidence remain. Retaining all evidence references
also respects the foreign key until the owning terminal incident bundle expires.

Keep the latest terminal run per target and strategy, plus the latest two scored successful runs
per target, strategy and locale. Successful means Completed or CompletedWithWarnings with a score
and finish time, matching the existing comparison reader. The two scored runs preserve the current
comparison when the latest terminal run failed. Archived runs participate as they do in the reader.
Items are removed before selected runs; target configuration and incident evidence are not changed.

## Logical-check and snapshot cleanup

LogicalCheckRetentionBatch is the final monitoring-detail cleanup stage. It selects completed
checks strictly older than 90 days only when hold/current-health/lease protections pass and no
result, SEO observation, certificate observation, attempt, durable work or incident evidence remains.
Findings and redirects cannot outlive their result because of their foreign keys. All incident
references protect the check until the owning evidence bundle is removed, including terminal ones.

The batch deletes the immutable snapshot before its check inside one transaction with local
retention permission. The deferred snapshot contract is satisfied when both are gone at commit.
Daily aggregates and monitor/endpoint configuration remain. The worker is still unscheduled.

## Aggregate expiration

Daily aggregates expire only for UTC dates strictly before the date 24 calendar months ago.
The cutoff day is retained in full. An aggregate must have a raw-deletion marker and no remaining
raw results for its monitor/day before it can expire; otherwise later raw cleanup would lose its
complete aggregate. Monitor/ancestor holds preserve the aggregate. A held logical check (including
checks protected through incident holds) preserves its monitor's aggregates conservatively because
its exact measured day may no longer be recoverable once raw detail has expired. Hold release or
expiry removes that exception. Aggregates use bounded single-monitor batches and leave registry
configuration intact.

## Robots cache expiration and policy preservation

The existing robots table stores one row per origin; refresh replaces the fetched contents in place,
so no separate superseded snapshot rows exist. Retention removes an expired cache row only when
FetchedAt and UpdatedAt are both strictly older than 90 days. Unexpired snapshots survive. Rows
carrying a sitemap requirement, configured sitemap URL or approved exception remain because those
fields are policy, not disposable fetched history. Refresh can recreate an expired default-policy
cache row using the existing origin workflow.

A hold in any supported scope preserves the related endpoint's origin cache, including historical
or archived endpoints. Origin matching uses an exact origin or slash boundary, not a hostname prefix.
Deletion shares the robots origin lock with refresh and rechecks eligibility after acquiring it.
The origin is never written to retention logs.

## Terminal incident bundles and recurrence

Resolved incidents expire strictly 24 calendar months after ResolvedAt; Closed incidents use
ClosedAt, so an explicit later closure starts the closure retention period. Active incidents and
active holds never expire. Pending, processing or retry-scheduled deliveries, and any delivery
lease, preserve the whole bundle. A held successor also preserves its immediate predecessor link.

Each batch locks selected incident rows, rechecks eligibility, and removes notification attempts,
deliveries, notification events, incident evidence and incident events before incident roots in one
transaction. This makes bundle deletion atomic and prevents a concurrent reopen from losing its
evidence. The batch root count and overall deadline are bounded by retention options.

Retained successors have PreviousIncidentId detached in the same transaction; RecurrenceCount is
unchanged and Version advances to protect optimistic concurrency. A system retention audit records
the old/new link and preserved recurrence count. Audit records have no age-based expiration.

## Hourly coordinator

The coordinator connects daily aggregate preparation and all eleven retention deletion categories. Enabled=true registers the hourly
monitoring-retention job on the existing maintenance queue, including when other schedulers are
disabled. Defaults remain Enabled=false and DryRun=true. Configuration belongs to the project
Administrator/operator; no lower-role web action changes these values.

Each run has one shared cancellation deadline and a hard cap on attempted batches, including empty
ones. It visits categories in dependency order before repeating a pass. A pass with no deletion
stops; dry-run stops after one pass so it never counts the same candidate twice. Dry-run counts are
bounded samples, not a total backlog estimate. Very small MaximumBatchesPerRun values can stop
before reaching later categories; the default 20 permits a complete twelve-category pass.

Each batch receives a fresh dependency-injection scope and database context. Earlier commits survive
a later batch failure; the failed transaction rolls back. Hangfire automatic retries are disabled,
and the next scheduled run can resume eligible work. The job logs category/counts/budget state and
replaces failure diagnostics with a safe exception-type category. A disabled coordinator is a no-op,
including for an already queued job from a previous configuration.

Aggregate-backed reporting and the AC-14 acceptance suite are implemented. Keep deletion disabled until dry-run counts are reviewed for the target database; automated verification uses disposable databases only.

## Reporting across retained and archived detail

A completed monitor/day switches to its aggregate when exact duration samples are present or RawDeletionStartedAt is set. All raw samples from that monitor/day are excluded from reporting, including held samples, to prevent double counting. Before deletion, counts and exact percentiles come from the compact aggregate. After deletion starts, counts remain exact and percentiles use the frozen histogram with explicit dashboard and CSV disclosure.

Daily aggregates cannot reconstruct a partial UTC day. If a requested timestamp window cuts through
an archived boundary day, that day is omitted and the dashboard/CSV disclose the missing boundary.
No full day is silently added outside the requested window and no prorated counts are invented.
UTC-midnight boundaries include complete archived days within the existing 366-day maximum window.

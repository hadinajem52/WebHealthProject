# Retention policy and implementation record

Increment 6 is in progress. No retention worker is enabled yet.

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

The worker will default to disabled and dry-run, with batches of 1,000, at most 20 batches per run,
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
protected hold record. Expiry is evaluated at PostgreSQL microsecond precision. The web management
flow and worker enforcement remain pending.

## Administrator web flow

Administrators can open /Retention from monitoring diagnostics, create a hold using its scope
and record identifier, and release an active hold. The history shows active, expired and released
states in pages of 100 records. Expiry input is UTC. POST actions require antiforgery tokens and
server-side Administrator authorization; reason text is HTML-encoded. The form currently accepts
record IDs from detail-page addresses or exports rather than offering a searchable record picker.
Browser layout verification and worker enforcement remain pending.

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
Raw retained windows continue using their exact existing percentile calculation. Mixed or aggregate
windows must disclose approximation rather than presenting bucket estimates as exact percentiles.

## Daily aggregate storage

Each monitor/UTC-day row stores total and scheduled counts, eligible Healthy/Warning/Down counts,
excluded/maintenance/cancelled counts, duration count/sum/minimum/maximum and the versioned histogram.
Maintenance and cancellation counts are subsets of excluded samples, not additional totals.
Comparability identity is a SHA-256 digest of the represented configuration identities; IsComparable
records whether they describe one comparable population. Source range and first/last measurement
retain report provenance. The forthcoming writer must populate these fields from raw results.

ComputedAt records recomputation. RawDeletionStartedAt is set before the first raw deletion for
that day; once set, recomputation from remaining raw rows is forbidden. Reports must choose the
complete aggregate for such days instead of double-counting protected raw rows that remain.
Endpoint purge removes its daily aggregates before monitors. No retention worker is enabled yet.

## Aggregate recomputation

DailyAggregateWriter rebuilds one completed UTC day from raw result rows under the retention
transaction lock. It streams scalar result/snapshot fields, preserves existing uptime classification,
and includes only eligible Healthy/Warning duration samples. It supports an existing batch
transaction or owns one when called independently. Empty or current/future days are not written.

Configuration fingerprint, snapshot schema and truth generation form the comparability identity.
Distinct identities are hashed in deterministic database order without retaining a growing set in
memory. More than one identity marks the day non-comparable. Recomputing replaces counts rather
than incrementing them. Once RawDeletionStartedAt is present, recomputation returns without writing.
The deletion worker and historical report reader still need to consume these contracts.

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
The batch is not scheduled yet; durable work and other retention categories remain pending.

## Completed durable-work batches

ExecutionHistoryRetentionBatch shares the transaction, deadline and protected-check query between
attempts and durable work. Durable work must be Completed, updated strictly before the 90-day cutoff,
and have no work lease fields; its logical check must also qualify. Pending, failed and leased work
remain. Both deletion paths reapply eligibility when deleting the selected IDs. The service remains
unscheduled, and the remaining retention categories and coordinator are still pending.

## Raw-result batches

RawResultRetentionBatch handles results measured strictly before the 90-day cutoff whose completed
checks pass the shared hold, lease, current-health and active-incident protections. Retained SEO or
certificate observations also preserve the result used by their readers. Observation expiration
and comparison-baseline protection remain separate pending work.

Each batch selects at most BatchSize result IDs from one monitor and UTC day. Before its first
real deletion, it recomputes the full daily aggregate and records RawDeletionStartedAt in the same
transaction. Subsequent batches preserve that aggregate instead of recomputing from partial raw
history. Findings and redirect hops are removed before their selected results; logical checks and
snapshots remain. Dry-run does not write aggregates or deletion markers. This service is not yet
scheduled; report integration and the remaining retention categories must be completed before
retention is enabled.

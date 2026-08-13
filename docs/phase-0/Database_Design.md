# PostgreSQL Foundation Design

**Owner:** Intern  
**Status:** Phase 0 design; migrations are implemented and tested in later phases
**Approval:** Approved by the intern/project owner on 2026-08-13

This document defines the correctness-critical data foundation. Later feature schemas receive only short notes until their owning phase.

## 1. Conventions

- Application-generated UUID primary keys.
- PostgreSQL `timestamptz` for UTC instants; IANA timezone identifiers stored separately.
- Reporting windows use `[start, end)` boundaries.
- Statuses use bounded text with `CHECK` constraints unless measurements justify another representation.
- Mutable configuration uses audit timestamps/actors and `version bigint` optimistic concurrency.
- Configuration with history is soft-deleted; historical rows are not cascade-deleted.
- Bounded, allow-listed diagnostics only; no secrets, sensitive headers, or complete bodies.

## 2. Core model

```mermaid
erDiagram
    CLIENT ||--o{ WEBSITE : owns
    WEBSITE ||--o{ ENVIRONMENT : has
    ENVIRONMENT ||--o{ ENDPOINT : contains
    ENDPOINT ||--o{ ENDPOINT_MONITOR : enables
    ENDPOINT_MONITOR ||--o{ LOGICAL_CHECK : schedules
    LOGICAL_CHECK ||--o| CHECK_RESULT : produces
    CHECK_RESULT ||--o{ FINDING : contains
    ENDPOINT_MONITOR ||--o{ INCIDENT : raises
    INCIDENT ||--o{ INCIDENT_EVENT : records
    INCIDENT_EVENT ||--o{ NOTIFICATION_EVENT : triggers
    NOTIFICATION_EVENT ||--o{ NOTIFICATION_DELIVERY : delivers
    APP_USER ||--o{ AUDIT_EVENT : performs
```

### Identity and assignment

| Entity | Important data |
|---|---|
| `app_user` | Identity fields, display name, disabled state, security stamp |
| `team`, `team_member` | Named group and effective-dated membership |
| `owner_subject` | Exactly one user or team reference |
| `contact_point` | Channel, display recipient, normalized recipient, enabled state |

These support the product's role/assignment behavior. They do not imply a multi-person project team.

### Registry

| Entity | Important data |
|---|---|
| `client` | Name/normalized name, active/deletion state, support assignment, version |
| `website` | Client, name/normalized name, assignment, enabled/deletion state, version |
| `environment` | Website, name/type, production flag, base URL, policy profile, version |
| `endpoint` | Environment, display/normalized URL and hash, assignment override, enabled state, HTTPS exception evidence, version |
| `endpoint_monitor` | Endpoint, monitor type, policy/overrides, schedule anchor, next due, configuration fingerprint |
| `tag`, `website_tag` | Normalized tag and unique website/tag relation |
| `target_authorization` | Personal ownership/permission evidence, effective/expiry/revocation state, endpoint and redirect host/port scope |

An endpoint represents one URL in one environment. Child monitors represent HTTP, SSL, SEO, and other monitor types.

### Scheduling and evidence

| Entity | Important data |
|---|---|
| `logical_check` | Stable ID, endpoint monitor, Scheduled/Manual/Urgent source, schedule/request time, initiator, state, cadence key, policy fingerprint |
| `execution_attempt` | Logical check, attempt number, job/worker reference, times, infrastructure outcome |
| `check_result` | One per logical check; outcome, failure category, status, timings, lengths, maintenance/uptime flags, safe diagnostic |
| `redirect_hop` | Ordered normalized from/to URLs, status, loop flag |
| `finding` | Rule, severity, bounded observed/expected values, stable issue key |
| `issue_state` | Endpoint monitor/issue key and failure/recovery counters |
| `endpoint_health` | Confirmed current status and evidence reference |
| `execution_lease` | Endpoint monitor key, owner token, fencing generation, acquisition/expiry, logical check |
| `durable_work` | Kind, dedupe key, queue/state, availability, lease, attempts, safe failure |

`check_result.logical_check_id` is both primary and foreign key, enforcing at most one terminal sample per logical check.

### Incidents, audit, and notifications

| Entity | Important data |
|---|---|
| `incident` | Endpoint monitor, issue key, severity/status, assignment, recurrence, lifecycle times, resolution, version |
| `incident_event` | Ordered append-only actor/system timeline with state change and bounded note |
| `incident_evidence` | Independent immutable snapshot plus copied logical-check identifier; survives raw-result deletion |
| `notification_event` | Type, occurrence key, template version, suppression, typed source |
| `notification_delivery` | Event, channel, immutable normalized recipient, state, retry/lease/sent fields |
| `notification_attempt` | Delivery attempt and normalized transport outcome |
| `audit_event` | Actor/system, time, action, entity, correlation, allow-listed before/after values |

The result, health, incident, timeline, and pending/suppressed notification records commit in one PostgreSQL transaction. SMTP delivery happens afterward.

## 3. Normalization

Normalization is versioned. Existing identities are never silently recomputed.

### Names and tags

Preserve a trimmed display value. Normalize to Unicode NFC, collapse whitespace, apply invariant case folding, and store a normalized value. Active client names are globally unique; website names are unique inside a client; tags are deduplicated.

### URLs

- Require absolute HTTP/HTTPS and reject user information.
- Lowercase scheme and IDNA ASCII host; remove final host dot and default port.
- Empty path becomes `/`; resolve dot segments and remove fragments.
- Preserve path case and trailing-slash distinction.
- Decode percent-encoded unreserved characters; normalize remaining escape casing.
- Preserve significant query values/order for endpoint identity.
- Store bounded normalized text, SHA-256 hash, and normalization version.
- Apply the same process to every redirect.

Crawl normalization additionally removes approved tracking parameters and applies the crawl profile's query policy.

### Recipients

Trim, normalize the IDNA domain, preserve the display address, and use a case-insensitive normalized company address for deduplication.

### Issue keys

Use `v1|monitor-type|rule-key|normalized-discriminator`. Exclude timestamps, severity, diagnostics, and mutable labels. Endpoint/monitor remain separate uniqueness columns.

## 4. Core statuses

- Health: `Unknown`, `Healthy`, `Warning`, `Critical`, `Maintenance`, `Disabled`. Maintenance overlays rather than destroys confirmed health.
- Logical check: `Pending → Queued → Running → Completed`; retry returns the same logical check to queued work. Terminal timeout/cancel/disabled outcomes still produce one result.
- Incident: `Open → Acknowledged → InProgress → MonitoringRecovery → Resolved → Closed`, with valid alternate transitions defined by BR-I rules. Closed reopening and forced closure require an administrator reason.
- Notification: `Pending → Processing → Sent`, with bounded `RetryScheduled`, `FailedPermanently`, and `Suppressed` paths.

## 5. Required PostgreSQL constraints

```text
client(normalized_name) WHERE deleted_at IS NULL
website(client_id, normalized_name) WHERE deleted_at IS NULL
endpoint(environment_id, normalized_url_hash) WHERE deleted_at IS NULL
endpoint_monitor(endpoint_id, monitor_type) WHERE deleted_at IS NULL
logical_check(endpoint_monitor_id, cadence_key) for scheduled work
check_result(logical_check_id)
execution_attempt(logical_check_id, attempt_number)
incident(endpoint_monitor_id, issue_key)
  WHERE status IN ('Open','Acknowledged','InProgress','MonitoringRecovery')
notification_event(source/type/occurrence_key)
notification_delivery(notification_event_id, channel, normalized_recipient)
durable_work(work_kind, dedupe_key)
```

Also enforce positive intervals/timeouts/limits, warning below critical thresholds, end after start, one user-or-team owner subject, required production HTTP exception evidence, and valid resolution data.

Indexes prioritize due monitors, endpoint history, active incidents, pending notifications/work, current health, audit search, and report time windows. Partitioning is not part of the initial design.

## 6. Concurrency and idempotency

- EF updates include the original `version`; stale updates fail and require reload.
- Lease acquisition is atomic and includes token plus fencing generation; final writes verify ownership.
- Scheduler claims due rows with `FOR UPDATE SKIP LOCKED`, creates a stable logical check and durable work, advances cadence, commits, then enqueues.
- Reconciliation can enqueue ambiguous work again because consumers and database constraints are idempotent.
- Duplicate deliveries of a completed logical check are no-ops.
- Active-incident and notification uniqueness are final database defenses under competing workers.

## 7. Later-phase design notes

- Maintenance: concrete UTC occurrences and explicit scope joins; detailed recurrence/DST design in Phase 6.
- SSL: bounded certificate observations keyed by endpoint/fingerprint; detailed renewal behavior in Phase 5.
- SEO/crawler: extracted values only, bounded crawl runs, unique source-target pairs; detailed schemas in Phase 6.
- Retention: default raw and aggregate periods remain requirements, but hold/aggregate/batch schemas are detailed in Phase 7.
- Production backup, PITR, HA, and restoration are deployment concerns, not Phase 0 blockers for this personal project.

## 8. Immediate proof required

Before the foundation is considered stable, automated PostgreSQL tests must demonstrate:

- One lease winner and safe expiry/fencing.
- One final result per logical check.
- One active incident per endpoint/monitor/issue key.
- One notification per event/channel/recipient.
- Optimistic-concurrency conflicts.
- Normalized and soft-delete uniqueness.

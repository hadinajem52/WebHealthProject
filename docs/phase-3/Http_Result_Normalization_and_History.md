# HTTP Result Normalization and History

**Work item:** Phase 3 / WI-31 result and history increment  
**Rules:** BR-H01–BR-H10, BR-Q07  
**Acceptance criteria contribution:** AC-02 history model and AC-05 terminal redirect evidence. Scheduling and the user-facing history flow remain open.

## Delivered behavior

`HttpResultNormalizer` converts the bounded safe-transport observation into one deterministic outcome and zero or more typed findings. It implements:

- the default accepted `200–299` range plus snapshotted explicitly accepted statuses;
- unconditional server-error classification for `500–599`;
- bounded UTF-8 required-content matching with ordinal or ordinal-ignore-case behavior;
- the production HTTP rule: an HTTP production target must finish on HTTPS, with snapshotted Warning/Critical severity;
- `ResponseTooLarge` when the streaming transport observes the configured decoded-body limit plus its sentinel byte;
- stable DNS, Connection, TLS, Timeout, Cancellation, ClientError, ServerError, RedirectLoop, ExcessiveRedirects, ContentMismatch, and ResponseTooLarge categories;
- additional fail-closed categories for invalid configuration, destination policy, malformed redirects, HTTPS downgrade, and protocol failures.

The normalizer never returns or persists a body, header, cookie, query value, configured marker, or raw exception. Finding identity uses the versioned `v1|monitor-type|rule-key|normalized-discriminator` format and is independent of severity and diagnostics. Primary-category precedence is explicit rather than dependent on finding enumeration order.

## Redirect evidence

Redirects remain manually traversed by the application-owned safe transport. Every destination is normalized and authorized before connection. Loop identity uses the complete normalized URL, while persisted evidence removes the query and retains the normalized scheme, authority, and path.

Exactly the configured number of redirects may be followed. A limit of ten permits ten redirects and a final response; any eleventh redirect response terminates as `ExcessiveRedirects` before its `Location` is inspected. The hop leading to a repeated normalized URL is marked `is_loop`.

## Immutable policy snapshot

`HttpMonitoringHistory` extends `check_configuration_snapshot` with canonical accepted-status codes, the optional required marker and comparison mode, production HTTP finding severity, and decoded-body/redirect limits. PostgreSQL constrains status syntax, comparison/severity values, the 2 MiB maximum body bound, and the maximum of ten redirects. A versioned canonical fingerprint includes every effective snapshot value; accepted statuses are sorted and deduplicated before hashing. Existing monitors and snapshots are backfilled during upgrade, after which the existing immutability trigger protects the new fields.

## History model and transaction

The second Phase 3 migration is `HttpMonitoringHistory`. It adds:

- `check_result`: one row per logical check, normalized outcome/category, nullable HTTP and phase measurements, total duration, labeled lengths, truncation, source, uptime participation, and bounded safe diagnostic;
- `redirect_hop`: result-owned ordered query-free from/to evidence, redirect status, and loop marker;
- `finding`: result-owned stable rule, severity, bounded observed/expected values, and issue key.

The result primary key is also the logical-check foreign key. PostgreSQL enforces valid outcomes/categories, nonnegative measurements, result-owned findings/hops, unique hop order, and unique `(logical_check_id, issue_key, rule_key)` findings. No response-body column exists.

`IHttpCheckHistoryService` locks the logical check, treats an existing result as an idempotent no-op, verifies the running state, exact target, canonical policy fingerprint, and request-owned transport identity, then validates redirect continuity and the final destination. Target, policy, and malformed-result failures have distinct statuses. The current lease token and fencing generation are consumed with one conditional PostgreSQL update. Result, hops, findings, logical-check completion, and lease expiry commit atomically. Concurrent duplicate writers converge on one result.

Scheduled checks count for uptime; Manual and Urgent checks do not. Maintenance classification is deliberately absent until Phase 4.

## Verification evidence

- Unit tests cover default/configured statuses, the unconditional `5xx` rule, marker comparison, production HTTP-to-HTTPS, all requested safe categories, response truncation, explicit primary-category precedence, exact issue keys, and fingerprint sensitivity/canonical status ordering.
- Controlled TCP tests cover normalization-equivalent redirect loops, exact ten-hop success and eleven-hop failure, over-limit redirects without `Location`, streaming body limits, redirect authorization, cancellation, malformed responses, and safe transport failures.
- The PostgreSQL 18 gate proves clean application, Phase 1 and Phase 2 upgrade paths, repeat application as a no-op, request/result binding, policy and redirect-chain rejection without lease consumption, one result under concurrent writers, stale-lease rejection, ordered-hop uniqueness, result-owned findings, and zero persisted body columns.

## Explicitly deferred

This increment adds no incidents, issue counters, endpoint-health projection, maintenance records, or notifications. Hangfire scheduling, logical-check creation, attempts/retries, and the history UI remain later Phase 3 work. Phase 4 owns confirmation counters, current health, incidents, maintenance-aware behavior, and notification orchestration.

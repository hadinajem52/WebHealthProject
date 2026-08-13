# Acceptance and Business-Rule Traceability

**Status:** Planned evidence; no runtime criterion is complete in Phase 0.  
**Test notation:** U unit, I integration, C concurrency, P performance/resilience, M controlled UAT.
**Approval:** Approved by the intern/project owner on 2026-08-13

The tables cover AC-01–AC-15 and BR-A01–BR-Q07. Functional requirements FR-001–FR-013 map to the owning packages described by those rules. NFR traceability is in section 8; unnumbered workflow, data, email-content, and report obligations remain attached to owning packages and require stable issue-level IDs before implementation.

## 1. Acceptance criteria

| AC | Phase / work | Required evidence |
|---|---|---|
| AC-01 | 2 / WI-21 | App validation and PostgreSQL normalized uniqueness/FK tests; registry demo |
| AC-02 | 3 / WI-30/31 | Controlled scheduled check with logical ID, status, duration, UTC time |
| AC-03 | 4 / WI-41/42 | Two failures: one active incident, one opening event/fake delivery |
| AC-04 | 4 / WI-41/42 | First pass recovery monitoring; second resolves with one recovery event/delivery |
| AC-05 | 3 / WI-31 | Redirect loop terminates/stores chain and worker remains healthy |
| AC-06 | 5 / WI-50 | TLS fixtures and exact 30/15/7 boundaries with displayed evidence |
| AC-07 | 6 / WI-61 | Controlled noindex/wildcard robots findings, or explicit incomplete deferral |
| AC-08 | 6 / WI-62 | Scoped bounded crawl and source-target broken-link evidence, or deferral |
| AC-09 | 4 / WI-40; regression 6 | Retained maintenance result, suppressed record, escalation behavior |
| AC-10 | 2 / WI-20/21 | Direct-request role/assignment matrix, no mutation, safe denial audit |
| AC-11 | 5 / WI-51 | Record identity for authorized screen/chart/CSV filters |
| AC-12 | 4 / WI-30/41/42 | Restart, duplicate, enqueue-after-commit, lease expiry; no duplicates |
| AC-13 | 2+4 / WI-20/41 | Configuration and incident audit/timeline by actor/time/entity |
| AC-14 | 7 / WI-70 | Retention before/after preserving active incidents, holds, aggregates, names |
| AC-15 | All; final 7 / WI-71 | CI run with unit/integration/migration/security suites |

## 2. Identity and registry

| Rule | Phase/work | Planned evidence |
|---|---|---|
| BR-A01 | 2/WI-20 | I: anonymous MVC redirect and API 401/403 |
| BR-A02 | 2/WI-20 | I: unauthorized direct mutation rejected, no change, safe audit |
| BR-A03 | 2/WI-20 | I: non-admin user/role/disable/reset matrix |
| BR-A04 | 2/WI-20 | I: disabled sign-in/session invalidation, history retained |
| BR-A05 | 2/WI-20 | I: Identity policy, hash-only persistence, secret-safe logs |
| BR-A06 | 2/WI-20 | I: append-only audit query by date/user/action/entity |
| BR-W01 | 2/WI-21 | U/I: trim/case normalization and DB duplicate rejection |
| BR-W02 | 2/WI-21 | I: client FK and per-client name uniqueness |
| BR-W03 | 2/WI-21 | I: enable rejected without environment |
| BR-W04 | 2/WI-21 | U/I: absolute HTTP(S); reject credentials/relative/file/FTP |
| BR-W05 | 2/WI-21 | I: production HTTP blocked or admin exception audited |
| BR-W06 | 2/WI-21 | U/I: equivalent host/default-port normalization and uniqueness |
| BR-W07 | 2/WI-21 | I: soft delete hides active, preserves history |
| BR-W08 | 2/WI-21/30 | I: disable prevents work; queued work safely skipped |
| BR-W09 | 2/WI-21 | U/I: endpoint override then website owner and assignment access |
| BR-W10 | 2/WI-21 | U/I: trimmed/deduplicated tags and filters |

## 3. Scheduling and HTTP

| Rule | Phase/work | Planned evidence |
|---|---|---|
| BR-S01 | 3/WI-30 | I: only enabled website/endpoint monitors become due |
| BR-S02 | 3/WI-30 | U: production/non-production interval, override, cadence anchor |
| BR-S03 | 3/WI-30 | C: one endpoint/monitor lease winner |
| BR-S04 | 3/WI-30/31 | I: timeout becomes bounded terminal result |
| BR-S05 | 3/WI-30 | C: retries/duplicate jobs share one logical ID/result |
| BR-S06 | 3/WI-30 | I: authorized manual queue, initiator/source, cadence unchanged |
| BR-S07 | 3/WI-30 | U/I: UTC ordering and timezone/DST display |
| BR-S08 | 3/WI-30 | I: one catch-up after downtime |
| BR-H01 | 3/WI-31 | I: timing/status/length/redirect values or unavailable marker |
| BR-H02 | 3/WI-31 | U/I: default 2xx and accepted statuses |
| BR-H03 | 3/WI-31 | I: manual per-hop redirect evaluation |
| BR-H04 | 3/WI-31 | U: 4xx policy and critical 5xx |
| BR-H05 | 3/WI-31 | I: DNS/connect/TLS/timeout categories |
| BR-H06 | 3/WI-31 | U/I: exact hop limit and terminal classification |
| BR-H07 | 3/WI-31 | U/I: normalized loop detection |
| BR-H08 | 3/WI-31 | I: production HTTP→HTTPS policy |
| BR-H09 | 3/WI-31 | U/I: marker/case rules |
| BR-H10 | 3/WI-31 | I: bounded sensitive/large body not persisted |

## 4. Incidents, uptime, SSL, and performance

| Rule group | Phase/work | Per-rule planned evidence |
|---|---|---|
| BR-I01, BR-I02 | 4/WI-41 | U/I: second failure opens; fail-pass-fail resets |
| BR-I03, BR-I04 | 4/WI-41 | C/I: one active matching issue; distinct issue permitted |
| BR-I05, BR-I06 | 4/WI-41 | U/I: two-pass recovery; persisted evidence/durations |
| BR-I07, BR-I08, BR-I09 | 4/WI-41 | U/I: resolution data, append-only timeline, transition/force-close rules |
| BR-I10 | 4/WI-41 | U/I: exact 30-day recurrence boundary |
| BR-U01, BR-U02, BR-U03 | 5/WI-51 | U/I: logical eligible samples and exclusion counts |
| BR-U04 | 5/WI-51 | U/I: exact `[start,end)` UTC boundaries |
| BR-U05, BR-U06 | 5/WI-51 | U/I: successful percentile inputs; current health/history distinction |
| BR-C01, BR-C02, BR-C03 | 5/WI-50 | I: HTTPS applicability, evidence, validation categories |
| BR-C04 | 5/WI-50 | U/I: exact 30/15/7 boundaries |
| BR-C05, BR-C06, BR-C07 | 5/WI-50 | C/I: fingerprint uniqueness, renewal, daily/urgent scheduling |
| BR-P01 | 5/WI-52 | U/I: total/TTFB ms, timestamps, missing values |
| BR-P02, BR-P03 | 5/WI-52 | U/I: 1500/3000 boundaries, overrides, third-breach/reset |
| BR-P04, BR-P05 | 5/WI-52 | U/I: 2 MiB/measurement label, provenance/comparability warning |

## 5. Maintenance and notifications

| Rule | Phase/work | Planned evidence |
|---|---|---|
| BR-M01 | 4/WI-40 | U/I: scope/range/timezone/reason/creator validation |
| BR-M02 | 4/WI-40 | I: checks continue, marked result, suppression record |
| BR-M03 | 4/WI-40 | U/I: existing incident open, escalation pause accounted |
| BR-M04 | 4/WI-40 | U/I: post-maintenance failure counter resets |
| BR-M05 | 6/WI-60 | U/I: recurrence expansion across DST gaps/overlaps |
| BR-N01 | 4/WI-42 | I/C: opening only on incident creation |
| BR-N02 | 4/WI-42 | U/I: endpoint/website/client/escalation recipients |
| BR-N03 | 4/WI-42 | C: unique event/channel/normalized recipient |
| BR-N04, BR-N05 | 4/WI-42 | U/I: reminder/ack cancellation and escalation/timeline |
| BR-N06 | 4/WI-42 | I: no first-pass recovery; confirmed recovery content |
| BR-N07 | 4/WI-42 | I: SMTP failure independent, retry/diagnostics |
| BR-N08 | 4/WI-42 | I: template allowlist excludes unsafe fields |

## 6. SEO and crawler

| Rule group | Phase/work | Per-rule planned evidence |
|---|---|---|
| BR-E01 | 6/WI-61 | I: successful HTML only; binary N/A |
| BR-E02, BR-E03 | 6/WI-61 | U/I: title/description missing/duplicate/disabled |
| BR-E04, BR-E05 | 6/WI-61 | U/I: canonical and production noindex/exception |
| BR-E06, BR-E07, BR-E08, BR-E09 | 6/WI-61 | U/I: root robots, groups/comments/wildcard, sitemap, environment policy |
| BR-E10 | 6/WI-61 | I: extracted values without full HTML |
| BR-L01, BR-L02 | 6/WI-62 | U/I: seed/host/path and robots override authorization |
| BR-L03, BR-L04 | 6/WI-62 | U/I: normalization/revisit/query/tracking handling |
| BR-L05 | 6/WI-62 | I/P: page/depth/concurrency/rate/duration/reason |
| BR-L06, BR-L07, BR-L08 | 6/WI-62 | I/C: classifications, source-target uniqueness, external no-recursion |
| BR-L09, BR-L10 | 6/WI-62 | I: user-agent/contact and partial cancellation evidence |

## 7. Reporting, retention, and security

| Rule | Phase/work | Planned evidence |
|---|---|---|
| BR-R01 | 5/WI-51 | I: current visible totals, shared filters, as-of |
| BR-R02 | 5/WI-51 | I: required filters and screen/export identity |
| BR-R03 | 5/WI-51 | I: UTF-8, stable columns, Unicode, quoting, ISO-8601, formula safety |
| BR-R04 | 2/WI-20 | I: append-only/reconstructable safe audit |
| BR-R05 | 7/WI-70 | I: exact raw/aggregate/incident eligibility |
| BR-R06 | 7/WI-70 | I: holds survive and action logged |
| BR-R07 | 2+5/WI-21/51 | I: historical names and exclusion from active counts |
| BR-Q01 | 3/WI-31 | I: prohibited IPv4/IPv6/private/metadata and redirects |
| BR-Q02 | 3/WI-31 | I: per-hop DNS/actual address and rebinding |
| BR-Q03 | 1+2/WI-10/20 | I: secret configuration and repository/log/artifact scan |
| BR-Q04 | 3+5/WI-31/50 | I: invalid TLS rejected; production bypass impossible |
| BR-Q05 | 2/WI-20/21 | I: markup values render as text |
| BR-Q06 | 2/WI-20/21 | I: forged MVC/unauthorized API requests rejected |
| BR-Q07 | 3+7/WI-31/71 | I/P: timeout, size, global/host concurrency, target load |

## 8. Functional and non-functional requirements

| IDs | Owning work | Planned evidence |
|---|---|---|
| FR-001, FR-012 | WI-20 | Identity, direct authorization, anti-forgery, audit |
| FR-002 | WI-21 | Registry validation, constraints, ownership, soft deletion |
| FR-003, FR-004 | WI-30, WI-31 | Asynchronous scheduling and normalized HTTP history |
| FR-005 | WI-50 | Certificate evidence and severity |
| FR-006 | WI-61 | Controlled SEO findings |
| FR-007 | WI-62 | Scoped bounded crawl and source-target results |
| FR-008 | WI-41 | Incident confirmation, lifecycle, recurrence |
| FR-009 | WI-42 | Durable recipient resolution, suppression, escalation, delivery |
| FR-010, FR-011 | WI-51 | Authorized dashboard/reports and matching CSV |
| FR-013 | WI-10, WI-71 | Health endpoints and protected operational diagnostics |
| NFR-01 | WI-30, WI-31, WI-71 | 500 endpoints, 100 bounded checks, no duplicate execution |
| NFR-02 | WI-51, WI-71 | Representative dashboard under 3 seconds P95 |
| NFR-03 | WI-10, WI-30, WI-42 | Persistent worker operation independent of sessions |
| NFR-04 | WI-31, WI-42 | Explicit deadlines, cancellation, and bounded data |
| NFR-05 | WI-10 and all operational work | Structured correlation identifiers and allow-listed fields |
| NFR-06 | WI-10, WI-71 | Liveness, readiness, worker/DB/queue diagnostics |
| NFR-07 | WI-10, WI-71 | Versioned controlled clean/upgrade migrations |
| NFR-08 | WI-10, WI-71 | Environment configuration and secret scans |
| NFR-09 | WI-11 and all UI work | Keyboard, labels, contrast, zoom, non-color cues |
| NFR-10 | WI-21, WI-51 | Configured display timezone and ISO-8601 export |
| NFR-11 | WI-30, WI-41, WI-42 | Restart, lease, reconciliation, idempotency |
| NFR-12 | All rule work; WI-71 gate | Deterministic critical-rule automated tests |

## 9. Completeness rule

If AC-07/08 are deferred, BR-E01–E10 and BR-L01–L10 must be explicitly listed as incomplete follow-up. BR-M05 requires a separate decision. No rule is complete from a planned test alone.

# Prioritized Delivery Backlog

**Owner:** Intern  
**Status:** Prioritized personal-project backlog
**Approval:** Approved by the intern/project owner on 2026-08-13

## 1. Work-item definition

Every implementation issue must include:

- Work-item ID, title, priority, phase, dependencies, and estimate.
- Business-rule and acceptance-criterion IDs.
- User-visible behavior and server-side authorization policy.
- Inputs, outputs, validation, error and boundary behavior.
- Data/schema/migration and mixed-version impact.
- Security/privacy and trust-boundary impact.
- Unit, integration, contract, concurrency, performance, and manual tests as applicable.
- Structured logs, metrics, traces, health/operational signals, and safe fields.
- Documentation and traceability impact.
- Compatibility, local/demo rollout, recovery concern, and demonstration evidence where applicable.
- Definition of done: code, migration, tests, docs, self-review, and linked evidence.

## 2. Phase 0 work

| ID | Priority | Work package | Requirements | Output / gate |
|---|---:|---|---|---|
| P0-01 | P0 | Personal scope and decisions | All | Sole ownership, project boundary, MVP, deferrals, deviations |
| P0-02 | P0 | Application role and assignment model | BR-A01–A06, W09, N02, R01–R04, AC-10/13 | Role/assignment matrix and schema semantics |
| P0-03 | P0 | Configuration, manual-check, and time semantics | BR-S02/S06/S07, H02/H06/H09, I01/I05, C04/C07, P02–P05, M01/M04/M05, N04/N05, R05 | Typed scope/precedence catalogue and boundary tests |
| P0-04 | P0 | Database/domain design | All data-bearing rules, AC-01–14 | Reviewable entities, constraints, indexes, leases, retention |
| P0-05 | P0 | Threat model and network policy | BR-Q01–Q07, W04/W05, H03/H06/H07/H10 | SSRF policy, safe transport, controlled tests |
| P0-06 | P0 | Controlled tests and delivery gates | AC-01–15 and all BRs | Reproducible fixture and CI strategy |
| P0-07 | P0 | Purity UI Dashboard Figma UI direction | NFR-09, UI portions AC-01/10/11/13 | Figma baseline, implementation styles, wireframes, accessibility contract |
| P0-08 | P0 | Immediate feasibility spikes | BR-S03/S05, I03, N03, Q01–Q04, C02/C03 | Dependency, safe HTTP, TLS, and PostgreSQL concurrency evidence |

## 3. Implementation work packages

These packages are ordered by dependency. They are not Ready until decomposed with the template above.

| ID | Phase | Priority | Package | Principal requirements | Acceptance evidence |
|---|---:|---:|---|---|---|
| WI-10 | 1 | P0 | Solution/runtime foundation | Enables all; NFR-03–08 | Build/start, health, PostgreSQL clean migration, test host/containers, no secrets |
| WI-11 | 1 | P0 | Purity UI Dashboard MVC shell | NFR-02, NFR-09 | Figma-aligned application styles, accessible layout/errors, responsive smoke tests |
| WI-20 | 2 | P0 | Identity, authorization, ownership, audit | BR-A01–A06, Q03/Q05/Q06, R04 | Direct-role tests, anti-forgery, disabled sessions, safe audit |
| WI-21 | 2 | P0 | Registry and target authorization | BR-W01–W10, R07 | AC-01 plus PostgreSQL constraint/concurrency/soft-delete evidence |
| WI-30 | 3 | P0 | Scheduling, logical checks, leases, durable work | BR-S01–S08 | AC-02, duplicate/restart/lease/catch-up evidence |
| WI-31 | 3 | P0 | Safe HTTP transport and history | BR-H01–H10, Q01/Q02/Q04/Q07 | AC-05 and complete SSRF/redirect/limit fixtures |
| WI-40 | 4 | P0 | Minimum maintenance | BR-M01–M04 | AC-09 suppression, marked results, pause/reset behavior |
| WI-41 | 4 | P0 | Health and incidents | BR-I01–I10 | AC-03/04/12 incident, transition, recurrence, concurrency evidence |
| WI-42 | 4 | P0 | Durable notifications | BR-N01–N08 | One event/delivery in fake transport, retry/suppression/restart evidence |
| WI-50 | 5 | P0 | SSL monitoring | BR-C01–C07 | AC-06 exact boundaries, validation categories, renewal |
| WI-51 | 5 | P0 | Dashboard, uptime, reports, CSV | BR-U01–U06, R01–R03 | AC-11 identity, authorization, CSV safety, query plans, P95 |
| WI-52 | 5 | P0 | Performance rules | BR-P01–P05 | Exact thresholds, counters, page size, provenance/comparability |
| WI-60 | 6 | P2 | Recurring maintenance | BR-M05 | Detailed in Phase 6 or recorded incomplete deferral |
| WI-61 | 6 | P2 | SEO | BR-E01–E10 | AC-07 controlled findings or recorded incomplete deferral |
| WI-62 | 6 | P2 | Bounded crawler | BR-L01–L10 | AC-08 controlled crawl or recorded incomplete deferral |
| WI-70 | 7 | P2 | Aggregates, retention, holds | BR-R05–R06 | Detailed and tested in Phase 7 |
| WI-71 | 7 | P1 | Hardening and project release | All; AC-15 | CI, security, representative performance, documented limitations |
| WI-80 | 8 | P3 | Optional deployment/closeout | Deployment only if pursued | Local/demo release or separately designed production deployment |

## 4. Definition of Ready

- Applicable IDs and expected behavior are explicit.
- Authorization/assignment and trust boundaries are decided.
- Validation, errors, limits, and boundary cases are defined.
- Data/migration/compatibility impact is understood.
- Tests and acceptance evidence are specified.
- Dependencies and unresolved decisions are recorded.

## 5. Definition of Done

- Behavior and authorization are enforced server-side.
- Constraints/migration are included and tested where applicable.
- Success, boundary, failure, concurrency, and abuse paths have proportionate tests.
- Logs/diagnostics are structured, useful, and allow-listed.
- No secret, full response body, debug output, or TLS bypass is committed.
- UI work passes responsive/accessibility review.
- Documentation and traceability are updated.
- Diff is reviewed for unrelated changes.
- Controlled demonstration and required automated checks pass.

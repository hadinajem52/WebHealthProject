# Phase 0 Checklist

**Owner:** Intern  
**Status:** Reopened for owner review after database-design revision
**Previous approval:** The 2026-08-13 approval predates the revised database design
**Project classification:** Personal internship/portfolio project; not enterprise-grade or production-certified

## Scope and foundation

- [x] Personal-project boundary and sole ownership recorded in [`Scope_and_Decisions.md`](Scope_and_Decisions.md).
- [x] Protected MVP and explicitly deferred scope recorded.
- [x] Application role/assignment behavior defined independently from project ownership.
- [x] PostgreSQL and the Figma-based UI baseline deviations recorded.
- [x] Production operations and later-phase design work explicitly deferred.

## Data and correctness

- [x] Core entities and relationships documented in [`Database_Design.md`](Database_Design.md).
- [x] Name, URL, recipient, issue-key, and crawl-pair normalization documented.
- [x] PostgreSQL uniqueness constraints and indexes proposed.
- [x] Optimistic concurrency, logical-check idempotency, leases, durable work, and incident deduplication documented.
- [x] Core status models and deletion behavior documented.

## Security

- [x] Threat model and trust boundaries documented in [`Security_and_Operations.md`](Security_and_Operations.md).
- [x] IPv4/IPv6 prohibited-network and private-exception policy documented.
- [x] Actual-connection, redirect, TLS, proxy, timeout, and size requirements documented.
- [x] Server-side authorization and anti-forgery remain mandatory.
- [x] Local/demo secret and diagnostic handling documented.

## UI and delivery

- [x] Purity UI Dashboard Figma direction and implementation reference documented in [`UI_Direction.md`](UI_Direction.md).
- [x] Main responsive journeys and accessibility requirements documented.
- [x] Prioritized backlog recorded in [`Backlog.md`](Backlog.md).
- [x] AC/BR/FR/NFR traceability recorded in [`Traceability_Matrix.md`](Traceability_Matrix.md).
- [x] Controlled HTTP/TLS/DNS/proxy targets and basic CI/test strategy documented in [`Test_and_Delivery_Strategy.md`](Test_and_Delivery_Strategy.md).

## Immediate feasibility checks

- [x] Pin supported GA dependency versions and prove Hangfire/PostgreSQL compatibility. Evidence: [`global.json`](../../global.json), [`Directory.Packages.props`](../../Directory.Packages.props), and SP-01 results in [`Test_and_Delivery_Strategy.md`](Test_and_Delivery_Strategy.md).
- [x] Prove actual-connection enforcement, redirects, and DNS-rebinding fixtures. Evidence: SP-02/SP-03 tests and results in [`Test_and_Delivery_Strategy.md`](Test_and_Delivery_Strategy.md).
- [x] Prove core PostgreSQL lease, logical-result, active-incident, and notification uniqueness under competing transactions. Evidence: SP-04 test and results in [`Test_and_Delivery_Strategy.md`](Test_and_Delivery_Strategy.md).
- [x] Record actual commands/results and resulting design changes in [`Test_and_Delivery_Strategy.md`](Test_and_Delivery_Strategy.md).

## Exit gate

- [x] Immediate feasibility checks pass or have a concrete Phase 1 action that does not invalidate the foundation.
- [x] Purity UI Dashboard Figma baseline and implementation reference are confirmed.
- [x] No unresolved decision requires restructuring the Phase 1 solution. Confirmed by the intern/project owner on 2026-08-13 after reviewing the completed foundation and feasibility evidence.
- [ ] Intern reviews the revised [`Database_Design.md`](Database_Design.md), records approval or requested changes, and then restores Phase 0 complete status.

Optional peer/mentor review is useful but not required to complete this personal-project phase.

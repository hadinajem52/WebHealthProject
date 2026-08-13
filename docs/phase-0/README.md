# Phase 0 — Foundation Design

**Status:** Reopened for owner review after database-design revision
**Owner:** Intern (sole project owner, implementer, reviewer, and operator)  
**Approval:** Approved by the intern/project owner on 2026-08-13  
**Project type:** Personal internship/portfolio project; not enterprise-grade and not production-certified

## Purpose

Phase 0 demonstrates that the intern understands the problem, has designed the correctness-critical foundation, and has identified how to test the riskiest immediate assumptions.

The Administrator, Operations, Developer/Support, and Viewer roles are application personas used to design and test authorization. They do not represent separate project stakeholders or staff.

## Phase 0 deliverables

| Artifact | Purpose | Status |
|---|---|---|
| [`Scope_and_Decisions.md`](Scope_and_Decisions.md) | Scope, sole ownership, core decisions, assumptions, and later-phase deferrals | Complete |
| [`Database_Design.md`](Database_Design.md) | Core entities, normalization, PostgreSQL constraints, concurrency, leases, and idempotency | Revised; owner re-approval pending |
| [`Security_and_Operations.md`](Security_and_Operations.md) | Threat model, SSRF/network policy, safe HTTP boundary, and personal-project operational notes | Complete |
| [`UI_Direction.md`](UI_Direction.md) | Purity UI Dashboard Figma direction, main journeys, and accessibility | Approved; implementation-ready |
| [`Backlog.md`](Backlog.md) | Prioritized implementation packages and completion criteria | Complete |
| [`Traceability_Matrix.md`](Traceability_Matrix.md) | Requirements mapped to phases and test evidence | Complete |
| [`Test_and_Delivery_Strategy.md`](Test_and_Delivery_Strategy.md) | Controlled targets, supported versions, local/CI tests, and immediate spikes | Complete |
| [`Phase_0_Checklist.md`](Phase_0_Checklist.md) | Evidence-backed Phase 0 completion status | Complete |

## Kept in Phase 0

- MVP versus deferred scope.
- Role and assignment behavior.
- URL normalization and core entity relationships.
- PostgreSQL uniqueness constraints.
- Logical-check idempotency, leases, and incident deduplication.
- SSRF and prohibited-network policy.
- Server-side authorization.
- Controlled HTTP test targets.
- Supported dependency-version decision process.
- Basic CI and testing strategy.
- Immediate feasibility work for dependency compatibility, safe HTTP connection enforcement, and PostgreSQL concurrency constraints.

## Explicitly deferred

Production hosting, high availability, production backup/PITR, enterprise secret vaults, managed logging/alerting, production Gmail operations, restore drills, production rollout governance, and large-scale load certification belong to later phases if the project is ever deployed beyond a local/demo environment.

Later features—advanced maintenance, SSL operations, SEO, crawling, long-term retention, and production rollout—receive short foundation notes in Phase 0 and detailed design in their owning phases.

## Evidence rules

- A checked item links to a repository artifact or actual command result.
- The intern records decisions and changes; no committee approval is required.
- Optional peer/mentor feedback is welcome but is not a Phase 0 blocker.
- Do not claim production readiness, enterprise readiness, independent review, or a test pass that did not occur.
- Never commit credentials, tokens, license keys, private certificates, or `.env` contents.

Phase 1 begins after [`Phase_0_Checklist.md`](Phase_0_Checklist.md) confirms that no unresolved decision blocks the application foundation.

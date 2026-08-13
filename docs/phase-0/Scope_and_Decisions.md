# Scope and Foundation Decisions

**Owner:** Intern — sole project owner, implementer, reviewer, and operator  
**Decision date:** 2026-08-13
**Approval:** Approved by the intern/project owner on 2026-08-13

## 1. Project boundary

This is a personal internship/portfolio project. It aims for professional engineering discipline and a secure demonstrable implementation, but it is not enterprise-grade, production-certified, independently audited, or supported by a multi-role operations team.

“Owner” has two meanings in the documentation:

- **Project owner:** the intern only.
- **Application owner/assignee:** a user or team record used by the product's role and incident workflows. These are domain concepts, not project staff.

The intern makes and records scope, architecture, security, data, UI, and release decisions. Optional external feedback does not transfer ownership or create a required approval chain.

## 2. Foundation decisions

- Architecture: modular monolith with thin MVC/controllers and Hangfire entry points.
- Runtime: .NET 10/ASP.NET Core MVC; use supported GA versions before adding production dependencies.
- Persistence: PostgreSQL through EF Core/Npgsql.
- Background work: Hangfire with a compatible PostgreSQL provider, pending the immediate compatibility spike.
- UI: Purity UI Dashboard Figma shell and design-system reference: <https://www.figma.com/design/cjTsi6qaX3bH0l3a4vF7Jm/Purity-UI-Dashboard---Chakra-UI-Dashboard--Community-?node-id=0-1&p=f&m=dev>.
- Monitoring: one application-owned `IHttpClientFactory` transport with manual redirects and actual-connection enforcement.
- Email: application-owned interface; recording fake for automated tests and personal Gmail only as an optional low-volume demo adapter.
- Time: UTC persisted instants; IANA timezone identifiers for display/scheduling policy.
- Correctness: PostgreSQL constraints, transactions, stable IDs, execution leases, optimistic concurrency, and idempotency records.

## 3. Protected MVP

- Authentication and server-side role/assignment authorization.
- Client, website, environment, endpoint, ownership, and audit management.
- Safe scheduled/manual HTTP checks with bounded redirects and durable history.
- Stable logical checks, leases, one terminal result, retry/restart safety, and reconciliation.
- Health confirmation/recovery, incident deduplication/lifecycle, and minimum maintenance behavior.
- Durable opening/recovery notification records and fake delivery tests.
- SSL status, operational dashboard, shared filters, and CSV consistency.
- Basic performance thresholds required by BR-P01–P05.
- Automated unit/integration tests and reproducible CI.

## 4. Deferred scope

- Daily summary email and additional escalation levels.
- User-specific timezone preferences beyond one configured display timezone.
- Separate web/worker processes until a measured need exists.
- Advanced recurring maintenance (BR-M05), SEO (AC-07/BR-E), and crawler (AC-08/BR-L) may be deferred from a core MVP and remain explicitly incomplete.
- Advanced retention/holds, long-window aggregation, partitioning, production backup/restore, and load certification are later-phase work.
- Enterprise hosting, HA, managed secret vaults, centralized logging/SIEM, on-call alerts, formal change management, and production handover are outside the personal-project baseline.

## 5. Application authorization model

- Administrator: global application administration.
- Operations: global operational actions, without user/role administration.
- Developer/Support: assigned targets/incidents only.
- Viewer: explicitly permitted read-only data.
- Every protected HTML, JSON, chart, CSV, manual-check, and incident endpoint enforces authorization server-side.
- Website configuration by Developer/Support remains disabled unless a separate permission is enabled.
- Effective endpoint assignee is endpoint override, otherwise website assignment.
- New incidents snapshot the effective assignee; later registry changes do not silently reassign an active incident.
- Disabled users grant no current access but remain referenced by history.

These roles must be implemented and tested even when the intern is the only real user, because direct-request authorization is a core learning and correctness requirement.

## 6. Configuration inheritance

`null` means inherit; explicit false/zero/empty values remain distinct. Persist effective configuration version and source with each logical check.

| Setting | Precedence |
|---|---|
| HTTP interval | Endpoint > Website > Global |
| HTTP timeout | Endpoint > Global |
| Availability/recovery/slow confirmation | Endpoint > Monitor Policy |
| Response warning/critical | Endpoint > specification default |
| Redirect hop limit | Endpoint > Global |
| SSL interval | Endpoint > Global |
| SSL warning bands | Client > Global |
| Reminder/escalation | Escalation Policy |
| Crawler limits | Run > Crawl Profile |
| Page-size warning | Endpoint |
| Retention | Global |
| Display timezone | User > Global |

## 7. Behavioral decisions

- Manual checks are visible evidence but do not change scheduled cadence, contractual uptime, or automated confirmation/recovery counters by default.
- Manual resolution moves an incident to `Resolved`; closure is separate.
- Maintenance overlays confirmed health rather than erasing it.
- A queued check revalidates enabled state before network access; disabled work becomes `SkippedDisabled`.
- Raw exports use UTC ISO-8601 `Z` timestamps.
- Notification acceptance is one durable event and one recording-fake delivery. SMTP cannot guarantee universal exactly-once delivery after ambiguous network failures.
- Only websites the intern owns or has explicit permission to test may be monitored. This is personal authorization evidence, not enterprise governance.

## 8. Phase 0 closure

The supported dependency versions are pinned, SP-01 through SP-04 pass, and the repeatable local PostgreSQL fallback is documented. On 2026-08-13, the intern/project owner confirmed that no unresolved decision required restructuring the Phase 1 solution. A later database-design review added missing protected-MVP entities and invariants; owner re-approval of that revision is now the only open Phase 0 documentation gate.

Production infrastructure decisions do not block Phase 1.

## 9. Change log

| Date | Decision |
|---|---|
| 2026-08-13 | Defined the project as personal, intern-owned, and not enterprise-grade. Removed multi-stakeholder approval assumptions. |
| 2026-08-13 | Kept correctness/security planning and moved production operations/later-phase proofs out of Phase 0. |
| 2026-08-13 | Replaced the prior vendor-template direction with the provided Purity UI Dashboard Figma baseline and recorded the design-system reference. |
| 2026-08-13 | Executed SP-01 through SP-04 successfully, pinned supported dependencies, and recorded the reproducible commands, results, limitations, and resulting design decisions. |
| 2026-08-13 | Confirmed no unresolved decision requires restructuring the Phase 1 solution and recorded Phase 0 as complete. |
| 2026-08-13 | Reopened Phase 0 owner review after revising the database design for scoped access, one-off maintenance, measurement provenance, immutable effective configuration, incident recurrence, and stronger PostgreSQL invariants. |

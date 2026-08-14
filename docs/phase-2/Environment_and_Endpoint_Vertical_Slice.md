# Environment and Endpoint Vertical Slice

**Work item:** Phase 2 / WI-21 (Environment and Endpoint portion)  
**Business rules:** BR-A06, BR-W02–W09, BR-R04, BR-R07  
**Acceptance criteria:** AC-01, AC-10, AC-13 partial

## Delivered behavior

- Environment list, details, create, edit, disable, soft-delete, restore, and manager-only archive flows.
- Environment names use shared name normalization and are unique per Website while not deleted.
- Supported types are Production, Staging, Preproduction, Test, Development, and Custom. `EnvironmentType = Production` and `IsProduction` are kept consistent in application code and by PostgreSQL.
- Endpoint list, details, create, edit, disable, soft-delete, restore, and archive flows.
- Endpoint ownership inherits the Website owner unless an enabled user/team override is selected. An override replaces inherited ownership rather than adding another authorization path.
- Administrator and Operations manage configuration. Developer/Support reads the single effective owner scope and may test only effectively eligible targets. Viewer reads only active grant scope and cannot test targets.
- Enabling an Endpoint requires active personal ownership or explicit testing-permission evidence scoped to its normalized host and effective port. Evidence can expire or be revoked.
- Operational queries exclude deleted records; archive queries are manager-only.

## URL and production safety

`EndpointUrlNormalizer` is the single endpoint identity implementation. Version 1:

- requires an absolute HTTP or HTTPS URI;
- rejects relative URLs, embedded credentials, fragments, unsupported schemes, and IPv6 zone identifiers;
- normalizes scheme, IDNA host, final host dot, default port, empty path, dot segments, percent-escape casing, and percent-encoded unreserved characters;
- preserves path case, trailing-slash identity, and significant query order/values;
- stores bounded display and normalized text plus a 32-byte SHA-256 hash and normalization version.

PostgreSQL provides the final unique constraint on Environment, URL hash, and normalization version. The service compares canonical text after a hash conflict so a collision is not silently treated as the same URL.

Production endpoints require HTTPS. HTTP is accepted only when an Administrator supplies or changes a non-empty reason of at most 500 characters. Approval actor/time are stored separately. Deferred PostgreSQL triggers reject Production HTTP evidence approved by a non-Administrator and reject changing an Environment to Production while an active HTTP endpoint lacks valid evidence.

The database revalidates the Administrator approver when an Endpoint becomes Production HTTP through an Environment transition, Environment reassignment, or URL scheme change. Historical evidence is not silently promoted into a valid Production exception.

The exception reason is shown only to Administrator and Operations users. Scoped Developer/Support and Viewer details expose only whether exception evidence exists.

## Phase 3 foundation

Creating an Endpoint also creates one `HttpAvailability` monitor referencing the seeded system policy profile. The record contains typed interval, timeout, confirmation, and threshold defaults plus a configuration fingerprint.

Monitor `IsEnabled` is its configured state and is not rewritten by Website, Environment, or Endpoint lifecycle changes. Effective monitoring eligibility is calculated centrally from active Website, Environment, Endpoint, monitor, and unexpired target-authorization evidence state.

This increment deliberately leaves `schedule_anchor` and `next_due_at` null. It performs no HTTP requests, DNS resolution, Hangfire enqueueing, scheduler work, logical checks, results, redirects, or history writes. Those remain Phase 3 responsibilities.

## Audit and concurrency

Every Environment and Endpoint mutation writes a typed audit event in the same transaction. Base URL, endpoint URL, HTTP-exception, and target-authorization changes use safe change flags and URL hashes rather than copying query values, exception reasons, or evidence-reference contents into audit JSON.

Every edit and lifecycle action uses the submitted original version. A conflict retains the stale token and requires reopening the edit form.

## Migration and verification

Migration `20260814115913_EnvironmentEndpointVerticalSlice` adds Endpoint, Endpoint Monitor, Policy Profile, endpoint-scoped grants, constraints, indexes, a deterministic default profile, and deferred cross-table enforcement. Migration `20260814123343_HardenTargetAuthorization` adds host/port-scoped authorization evidence, backfills normalized host/port identity, and hardens Production HTTP transition checks.

Verification covers:

- URL normalization/rejection boundaries and stable SHA-256 identity;
- clean PostgreSQL migration application;
- Environment and Endpoint uniqueness, lifecycle, concurrency, and typed audit actions;
- default monitor/profile creation with no scheduling timestamps;
- Administrator-only Production HTTP exceptions at service and database boundaries;
- policy-profile/monitor-type consistency;
- effective-owner override behavior, external target evidence, centralized monitoring eligibility, Developer testing authorization, and Viewer denial;
- endpoint-scoped Viewer grants and manager-only archive queries.

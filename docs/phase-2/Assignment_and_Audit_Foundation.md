# Assignment and Audit Foundation

**Completed:** 2026-08-14  
**Work item:** WI-20 foundation; registry-owned assignment remains in WI-21  
**Rules:** BR-A02, BR-A04, BR-A06, BR-R04; partial BR-W09 and AC-10/AC-13

## Delivered behavior

- Administrators can list, create, rename, disable, and manage the current members of assignment teams.
- Team membership is effective-dated. Removing a member closes the current period instead of deleting history.
- Disabled users cannot be newly assigned. Disabled teams confer no current access when assignment-aware registry policies consume this foundation.
- Every user and team has one reusable `owner_subject`; the migration backfills existing users and future bootstrap/managed users receive one automatically.
- `IAssignmentAccessEvaluator` resolves direct-user or current team membership while denying disabled users and disabled teams; future resource handlers can consume it without storing assignment claims in cookies.
- Administrator and Operations users can search audit history by UTC date, actor, action, and entity. Developer/Support and Viewer fail closed.
- User and team create/update operations record actor, UTC timestamp, action, entity, outcome, and allow-listed before/after values in the same transaction.
- Audit rows are append-only through a PostgreSQL update/delete trigger as well as the absence of application mutation APIs.

Registry targets and explicit Viewer access grants are not part of this increment because client, website, environment, and endpoint rows do not exist yet. The next registry increment will reference `owner_subject` and add resource-specific authorization.

## Inputs and errors

- Team names are required, limited to 200 characters, Unicode NFC-normalized, whitespace-collapsed, and invariant-case-normalized with normalization version 1.
- PostgreSQL enforces unique normalized team names independently of the form and service checks.
- A newly submitted member must identify an existing enabled user. An existing disabled member can be retained or explicitly removed without an unrelated edit silently changing membership history. Repeated user IDs are deduplicated.
- Member eligibility is checked under PostgreSQL user-row locks inside the mutation transaction, so a concurrent account disable is observed before a new membership can commit.
- A stale team version is rejected with a reload message and leaves the scoped DbContext usable for later work.
- Concurrent duplicate-name writes return the same safe validation message as an ordinary duplicate.
- Audit search uses inclusive UTC calendar dates, a maximum page size of 100, and newest-first stable ordering.

## Data and migration

`AssignmentAndAuditFoundation` adds:

- `team` with normalized identity, audit actors/timestamps, disabled state, and `version` optimistic concurrency;
- `team_member` with effective `[from, until)` membership history;
- `owner_subject` with an exactly-one-user-or-team check and partial unique indexes;
- JSONB `before_values` and `after_values` on `audit_event`;
- an entity/time audit search index;
- the `btree_gist`-backed exclusion constraint that rejects overlapping membership periods;
- the database trigger that rejects update or delete of an audit row.

All identity, assignment, actor, and ownership foreign keys use restrictive deletion. Membership and audit history are never cascade-deleted.

## Security and privacy

- Team administration uses the existing Administrator-only policy and anti-forgery filter.
- Audit history uses `ViewAuditHistory`, limited to Administrator and Operations.
- The mutation writer exposes only typed user/team audit methods. Callers cannot submit arbitrary keys, actions, entity types, or complex objects.
- Typed user snapshots contain only user ID, display name, email, disabled state, supported roles, and a password-reset boolean. Passwords, hashes, tokens, query strings, and arbitrary request bodies cannot cross the writer contract.
- Typed team snapshots contain only team ID, name, disabled state, and member user IDs.
- Sidebar visibility mirrors policies for usability; direct-request policies remain authoritative.

## Verification evidence

- Unit tests cover NFC, whitespace, and invariant-case name normalization.
- Direct-request tests cover the audit policy for all four roles and sidebar visibility.
- The disposable PostgreSQL 18 test applies all four migrations and verifies normalized uniqueness, owner-subject creation, disabled-member retention, concurrent-disable locking, effective membership closure, overlap rejection, stale-version rejection, searchable typed snapshots, and database-level append-only behavior.
- The delivery gate verifies locked restore, formatting, Release build, migration drift, tests, the accepted Testcontainers advisory, repository secret scanning, and whitespace.

## Next boundary

The registry increment must add clients, websites, environments, endpoints, and access grants; implement website owner inheritance and endpoint override; and evaluate Developer/Support assignments and Viewer grants against current, enabled user/team state.

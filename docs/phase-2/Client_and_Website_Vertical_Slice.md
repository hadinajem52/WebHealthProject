# Client and Website Vertical Slice

**Work item:** Phase 2 / WI-21 (client and website portion)  
**Business rules:** BR-A06, BR-W01, BR-W02, BR-W03, BR-W07, BR-W08, partial BR-W09  
**Acceptance criteria:** AC-01 partial, AC-10, AC-13 partial

## Delivered behavior

- Authorized list, details, create, edit, disable, soft-delete, and restore flows for clients and websites.
- Administrator and Operations can read and mutate the full registry. Developer/Support can read records owned by the user or an effective team membership. Viewer can read only records covered by an active grant. Client scope flows to its websites; website and environment grants expose only the containing website/client path.
- Developer/Support and Viewer mutations fail at the `ManageRegistry` policy and are recorded by the centralized forbidden-request audit path.
- Client and website details outside the caller's scope return not found, avoiding resource disclosure.
- Operational lists exclude soft-deleted records. Administrator and Operations use the separate Archived registry view for restore workflows; assignment-scoped roles cannot query the archive.
- Website ownership is required. Create and edit owner choices exclude disabled users and teams, while an existing disabled owner can remain during an unrelated edit.
- Website creation defaults to disabled. Enabling requires an active, non-deleted environment.

## Validation and persistence

- `NameNormalizer` is the shared trim, Unicode normalization, whitespace-collapse, and case-folding implementation.
- PostgreSQL partial unique indexes are the final duplicate defense: client names are global and website names are unique per client while the record is not soft-deleted.
- Owner, client, and actor relationships use restrictive foreign keys. Deletes set `deleted_at`/`deleted_by_user_id`; rows and history remain.
- Every edit and lifecycle POST sends the original `version`. EF Core compares that original value and returns a safe concurrency conflict without committing the audit event. The conflict response keeps the stale token, so retrying cannot silently overwrite the newer database state.
- Deferred constraint triggers check the enabled-website invariant at transaction commit. They reject both enabling without an environment and removing/deactivating the final active environment from an enabled website.
- Registry mutations and their typed, allow-listed audit snapshots commit in the same transaction. Note contents are deliberately excluded, while `NotesChanged` records whether an update changed them.

## Data impact

Migration `20260814110940_ClientWebsiteVerticalSlice` adds `client`, `website`, the minimal `environment` foundation needed for the enabling invariant, and scoped `access_grant`. Endpoint scope is intentionally deferred until the endpoint table exists.

The migration is explicit; the web application does not migrate its schema at startup.

## Verification

- Unit tests enforce the typed audit-writer surface and safe registry snapshot fields.
- Authorization integration tests cover registry read/manage policies for all four roles and navigation visibility.
- Native PostgreSQL verification covers clean migration application, shared normalization, global and per-client duplicate rejection, stale-version rejection, required environment behavior at both service and database boundaries, assignment/grant query scoping, lifecycle mutations, and audit actions.
- The normal delivery gate continues to scan for secrets and known vulnerabilities under the repository's accepted-advisory policy.

## Deferred scope

Environment CRUD, endpoint CRUD and owner override, tags, monitoring policy/default configuration, and grant administration UI remain later Phase 2 increments. Website enabling through the UI becomes practical when environment CRUD is delivered; the current service and database rule already fail closed.

# Administration and Authorization Baseline

**Completed:** 2026-08-14  
**Work item:** WI-20  
**Rules:** BR-A01 through BR-A05; AC-10 remains partial until resource-assignment combinations exist

## Delivered behavior

- Only the `Administrator` role can open the user administration routes or invoke their actions.
- Administrators can list users, create an account with an initial password, assign any supported application roles, update the display name, reset another user's password, and enable or disable an account.
- The four roles are fixed application personas. Administration assigns these roles; it does not create or rename security roles.
- An administrator cannot disable their own account or remove their own `Administrator` role.
- The last enabled administrator cannot be disabled or demoted.
- Disabling an account, changing its roles, or resetting its password changes the security stamp. Stale and disabled principals are rejected at the next configured five-minute security-stamp validation.
- The sidebar exposes user administration only to administrators. This is a usability rule; the controller policy remains the access-control boundary.

## Inputs and errors

- Display names are required and limited to 200 characters.
- Emails are required, validated as email addresses, limited to 256 characters, and remain unique through Identity/PostgreSQL constraints.
- Initial and replacement passwords use the configured Identity policy: at least 12 characters with upper case, lower case, number, symbol, and four unique characters.
- At least one of the four supported roles is required. Posted role names are allow-listed again in the service.
- Identity validation and concurrency errors return to the form without exposing a password value.
- A missing edit target returns HTTP 404. Unauthorized direct requests are challenged or forbidden before the controller executes.

## Data, security, and operational signals

- The `AuthorizationDenialAudit` migration adds the append-only `audit_event` foundation, restrictive actor foreign key, and actor/time, action/time, and time indexes.
- User creation, role changes, account state, password reset, and the security-stamp update share explicit database transactions. The last-administrator check runs in a serializable update transaction.
- Passwords are passed directly to Identity, persisted only as hashes, cleared from returned view models, and never included in structured logs.
- Administration logs contain only actor ID, target user ID, disabled state, supported role names, and whether a password reset occurred. Email addresses and password values are excluded.
- Authenticated forbidden requests create durable `authorization.denied` audit events through one centralized authorization-result handler. The allow-list contains actor ID, UTC timestamp, action, request path without query string, method, outcome, and correlation ID.
- Administration-change events, safe before/after values, and the audit query UI remain in the broader audit increment required by BR-A06 and AC-13.

## Authorization policies

| Policy | Current role baseline | Later assignment rule |
|---|---|---|
| `Administration` | Administrator | None; global administration only |
| `Diagnostics` | Administrator, Operations | None; detailed runtime diagnostics are global operational data |
| `OperateMonitoring` | Administrator, Operations | Target-specific actions will also enforce assignment where required |
| `ReadAllOperationalData` | Administrator, Operations | Developer/Support and Viewer require a future resource assignment/grant policy |

Developer/Support and Viewer deliberately fail closed for global operational data until registry ownership, teams, and access grants support resource authorization.

## Verification evidence

- `dotnet test WebHealthProject.sln --configuration Release --no-restore`: 52 total integration tests, 50 passed and 2 PostgreSQL opt-in tests skipped; 2 unit tests passed.
- `./scripts/run-database-foundation-tests.ps1`: disposable PostgreSQL 18 passed all three migrations, durable denial persistence, isolated role-only session invalidation, disable/password invalidation, role replacement, password hashing, and self-lockout protection. A repeated explicit migration update reported no pending migrations.
- Direct-request tests cover every role plus a roleless principal for administration, diagnostics, global operations, and global reads. They also prove a forbidden request reaches the audit writer without persisting its query string.

## Known boundaries

- Assignment-aware resource authorization cannot be completed until registry ownership and access-grant data exist.
- Administration create/update audit records and the authorized audit search view remain in the append-only audit increment.
- Email change, self-service password change, and account recovery are not required by this baseline.

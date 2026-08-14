# Authentication and Protected Shell

This document explains what task 2.1 currently implements.

## Short version

The application now uses ASP.NET Core Identity for users, roles, password hashing,
lockout, and sign-in cookies. Pages are protected by default. A user must sign in
before opening the operational application.

The database migration creates the Identity tables, but it does not create an
administrator or store a password. The first administrator is created separately
with the explicit `--bootstrap-admin` command and secret configuration.

## Main files

| Area | File | Purpose |
| --- | --- | --- |
| User | `src/WebHealth.Infrastructure/Identity/ApplicationUser.cs` | Adds `DisplayName`, `IsDisabled`, and timestamps to the Identity user |
| Role | `src/WebHealth.Infrastructure/Identity/ApplicationRole.cs` | Application Identity role with a `Version` concurrency field |
| Role definitions | `src/WebHealth.Infrastructure/Identity/ApplicationRoles.cs` | Names and stable IDs for the four application roles |
| Identity setup | `src/WebHealth.Infrastructure/DependencyInjection.cs` | Registers Identity, EF stores, password rules, and lockout rules |
| Database mapping | `src/WebHealth.Infrastructure/Identity/*Configuration.cs` | Maps Identity entities to application table names |
| Database model | `src/WebHealth.Infrastructure/Persistence/ApplicationDbContext.cs` | Changes the context to an `IdentityDbContext` |
| Migration | `src/WebHealth.Infrastructure/Persistence/Migrations/*_IdentityAccessAndAudit.cs` | Creates Identity, access-subject, team, and append-only audit tables in the `web_health` schema |
| Bootstrap | `src/WebHealth.Infrastructure/Identity/AdminBootstrapper.cs` | Creates roles and the initial administrator when explicitly requested |
| Login | `src/WebHealth.Web/Controllers/AccountController.cs` | Handles login and logout |
| Login UI | `src/WebHealth.Web/Views/Account/Login.cshtml` | Displays the sign-in form |
| Authorization | `src/WebHealth.Web/Program.cs` | Requires authentication by default and configures the cookie |
| Tests | `tests/WebHealth.IntegrationTests/AuthenticationShellTests.cs` | Tests protected pages, login visibility, antiforgery, and health endpoints |

## Users and roles

`ApplicationUser` inherits from `IdentityUser<Guid>`, so each user has a Guid ID
and the standard Identity fields. The project adds:

- `DisplayName`: the name displayed in the application shell.
- `IsDisabled`: prevents the user from signing in.
- `CreatedAt` and `UpdatedAt`: account timestamps.

`ApplicationRole` inherits from `IdentityRole<Guid>`.

The defined roles are:

- Administrator
- Operations
- Developer/Support
- Viewer

Their IDs are hard-coded in `ApplicationRoles.All`. This makes role identity
stable across runs and environments. The migration creates the role table, but
the roles themselves are created by the bootstrap command.

## Database tables

The Identity tables use the existing `web_health` PostgreSQL schema and names
such as:

```text
web_health.app_user
web_health.app_role
web_health.app_user_role
web_health.app_user_claim
web_health.app_role_claim
web_health.app_user_login
web_health.app_user_token
```

The migration contains table definitions and indexes only. It does not contain
an administrator password or role data.

## Bootstrap administrator

`AdminBootstrapper` reads these settings from the `BootstrapAdmin` configuration
section:

```text
BootstrapAdmin:Email
BootstrapAdmin:DisplayName
BootstrapAdmin:Password
```

The values must be supplied through secret configuration, for example user
secrets. The application only runs the bootstrap process when the command-line
argument is present:

```text
--bootstrap-admin
```

The bootstrap process:

1. Checks that the required secret settings exist.
2. Creates the four roles with their stable IDs if they do not exist.
3. Creates the administrator user if the email does not already exist.
4. Adds that user to the Administrator role.
5. Fails if the existing role has an unexpected ID or the account is disabled.

The password is passed to `UserManager.CreateAsync`. ASP.NET Core Identity stores
the resulting password hash in `password_hash`; it does not store the original
password.

## Sign-in and sign-out flow

`AccountController` exposes:

- `GET /Account/Login`: displays the public login page.
- `POST /Account/Login`: validates the form and signs the user in.
- `POST /Account/Logout`: signs the user out.
- `GET /Account/AccessDenied`: returns the shared safe 403 response for authenticated users without permission.

The login action rejects a missing user and a disabled user with the same generic
message. This avoids revealing whether a particular email belongs to an account.
Failed attempts use Identity lockout handling.

The login page uses `_AuthLayout.cshtml`. It reuses the project design tokens and
shared CSS, but intentionally does not render the authenticated sidebar. The
normal `_Layout.cshtml` displays the signed-in user and provides a POST sign-out
form.

Return URLs are restricted with `Url.IsLocalUrl`, preventing an external login
redirect such as `https://example.com`.

## Authorization behavior

The fallback authorization policy contains `RequireAuthenticatedUser()`. This
means a newly added MVC page is protected unless it explicitly opts out.

The current explicit anonymous endpoints are:

- `/Account/Login`
- static assets
- `/health/live`
- safe error and HTTP status pages, which contain only a correlation reference and no operational data

`/health/ready` remains protected. `UseAuthentication()` runs before
`UseAuthorization()`, allowing the authentication cookie to be read before the
authorization decision is made.

## Security settings currently configured

### Passwords

- Minimum length: 12 characters.
- At least 4 unique characters.
- Requires a digit, lowercase letter, uppercase letter, and non-alphanumeric character.

### Lockout

- Enabled for new users.
- Five failed attempts cause lockout.
- Lockout duration: 15 minutes.

### Authentication cookie

- HTTP-only.
- `SameSite=Lax`.
- HTTPS only.
- Eight-hour expiry.
- Sliding expiration enabled.
- Login path: `/Account/Login`.

### Security stamp

Identity revalidates the security stamp every five minutes. A changed stamp can
invalidate an existing authentication cookie.

### Antiforgery

MVC controllers use automatic antiforgery validation. This protects POST forms,
including login and logout, from cross-site request forgery.

## What the current tests prove

The integration tests currently prove that:

- An anonymous request to `/` redirects to login.
- The login page is public and uses the authentication layout.
- External return URLs are rejected.
- A login POST without an antiforgery token is rejected.
- `/health/ready` is protected.
- `/health/live` remains public.
- The authenticated shell displays the user name.
- Sign-out is a POST action.
- Both migrations apply to a clean PostgreSQL 18 database and a repeat update is a no-op.
- The bootstrap is idempotent, creates all four stable roles, stores a password hash rather than the supplied password, and grants Administrator.
- The application sign-in manager rejects a disabled user.

The test application uses a test authentication handler and the
`X-WebHealth-Test-User` header to simulate an authenticated user. That is useful
for testing protected shell rendering, but it is not the real password sign-in
flow.

## Remaining verification or follow-up

Dedicated tests should also prove:

- a real valid user can sign in and sign out;
- passwords do not appear in logs or audit records;
- the first API endpoints return `401` or `403` instead of an HTML login redirect.

ASP.NET Core 10 cookie authentication redirects browser-page challenges to
`/Account/Login`, but automatically returns `401` or `403` for endpoints it
recognizes as APIs. Recognized endpoints include `[ApiController]` actions,
minimal APIs that read or write JSON, typed-result endpoints, and SignalR. A
separate API authentication scheme is therefore not required by the current
framework behavior. When the first API endpoint is added, integration tests must
verify anonymous and authenticated-but-forbidden responses and ensure the
endpoint carries recognized API metadata. See the
[ASP.NET Core 10 API endpoint authentication behavior](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0).

## Local setup

Apply migrations explicitly, configure the bootstrap values as user secrets,
and run the one-shot bootstrap command:

```powershell
$env:WEBHEALTH_MIGRATIONS_CONNECTION = '<local PostgreSQL connection string>'
dotnet ef database update --project src/WebHealth.Infrastructure --startup-project src/WebHealth.Infrastructure

dotnet user-secrets set 'ConnectionStrings:WebHealth' '<local PostgreSQL connection string>' --project src/WebHealth.Web
dotnet user-secrets set 'BootstrapAdmin:Email' 'admin@example.test' --project src/WebHealth.Web
dotnet user-secrets set 'BootstrapAdmin:DisplayName' 'Administrator' --project src/WebHealth.Web
dotnet user-secrets set 'BootstrapAdmin:Password' '<strong local password>' --project src/WebHealth.Web
dotnet run --project src/WebHealth.Web -- --bootstrap-admin
```

Do not put the connection string or bootstrap password in committed settings.
The bootstrap command never applies migrations; it fails if the schema is not
already current.

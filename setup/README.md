# Running WebHealth on a fresh machine

`setup.ps1` at the repository root takes a clean clone to a running application with the same
database contents the project is developed against. It is meant to be run once, on a machine that
has never seen this project.

## Before you start

Install these two things. Nothing else is required.

| Requirement | Version | Where |
| --- | --- | --- |
| .NET SDK | 10.0.4xx | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| PostgreSQL | 18 | <https://www.postgresql.org/download/windows/> |

Accept the PostgreSQL installer defaults and keep the **Command Line Tools** component selected —
the script needs `psql` and `pg_restore`. Remember the password you give the `postgres` user.

PostgreSQL 18 is a hard requirement for the seeded database: the snapshot is a version 18 archive
and older tools cannot read it. If you already run an older server, use `-Seed Empty` (below) to
build the schema from migrations instead.

Node.js is **not** required.

The PNG audit's Phase 3 reference optimizer is bundled with the application. The repository pins
the official oxipng 10.2.0 Windows x64 and Linux x64-musl executables, so no machine-level oxipng
installation is required.

## Run it

From the repository root:

```
setup.cmd
```

Use `setup.cmd` rather than calling the `.ps1` directly. Windows blocks unsigned PowerShell scripts
by default, and the `.cmd` wrapper starts PowerShell with that restriction lifted for this one
script. If you would rather run the script yourself:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

The script asks for the `postgres` password and does the rest. It takes about a minute.

Arguments pass straight through the wrapper:

```
setup.cmd -PgPort 5432 -PgPassword "your postgres password"
```

## Then sign in

```powershell
dotnet run --project src\WebHealth.Web --launch-profile https
```

Open <https://localhost:7144> and sign in:

- **Email** `admin@example.test`
- **Password** `Hnjm1hnjm23_`

`setup.cmd -Run` starts the application as its last step, so you can do both in one command.

## What the script does

1. **Verifies prerequisites.** Confirms the .NET 10 SDK feature band pinned in `global.json`, then
   locates `psql` and `pg_restore`. It considers every copy on `PATH` and every installation under
   `C:\Program Files\PostgreSQL`, and picks the newest — an old PostgreSQL earlier on `PATH` does
   not hide a newer one.
2. **Finds the server.** Probes ports 5432, 6432 and 5433, then authenticates. It disables PostgreSQL
   passfiles so a successful probe always uses credentials the application can reuse. It first tries
   connecting without a password, for servers configured for trust authentication, and only then
   prompts. A wrong password gets three attempts rather than a stack trace.
3. **Creates the database.** Drops an existing one only after you confirm, then creates it fresh
   with UTF-8 encoding from `template0`. If the cluster refuses UTF-8 it retries with the cluster's
   own defaults and warns.
4. **Writes local configuration.** Generates the .NET user-secrets file for the
   `web-health-project-development` secrets id: the connection string built from the host, port and
   credentials it just validated, plus the bootstrap administrator, the PageSpeed Insights key and
   the Gmail SMTP settings. An existing secrets file is copied to a timestamped backup
   beside it first, so repeated runs never destroy the original.
5. **Restores build dependencies.** `dotnet tool restore` for the pinned `dotnet-ef`, a locked-mode
   NuGet restore, and a build of the web project. If the vendored Chart.js asset is somehow absent
   it falls back to `npm ci` and `npm run vendor`.
6. **Seeds the database.** Restores `setup/webhealth-seed.dump`, a complete `pg_dump` of the
   development database — schema, application data and Hangfire tables — applies any migrations
   added after the snapshot was captured, then runs `ANALYZE` so the query planner has statistics
   for the reporting pages.
7. **Bootstraps the administrator.** Runs the application's own `--bootstrap-admin` entry point.
   This creates the four application roles and the administrator when they are missing and leaves
   them alone when they are not, so it is safe on both a seeded and an empty database. It doubles
   as a check that the application itself can reach the database with the configuration just
   written.
8. **Trusts the HTTPS certificate.** The authentication cookie is `Secure`-only, so the application
   is unusable over plain HTTP. The script checks the ASP.NET Core development certificate and trusts
   it if needed without removing certificates used by other projects. Accept the Windows prompt if
   one appears.
9. **Verifies.** Prints the migration, user, website, endpoint and incident counts actually present
   in the new database, and warns if the application's usual ports are already taken.

While it runs, the script neutralises any `PGSERVICE`, `PGSSLMODE`, `PGOPTIONS`, `PGPASSFILE` and
similar variables that would otherwise redirect or authenticate `psql`, and restores them, along
with `PGPASSWORD`, before it exits — including when it fails.

## Options

| Option | Default | Purpose |
| --- | --- | --- |
| `-PgHost` | `127.0.0.1` | PostgreSQL host. |
| `-PgPort` | auto | Skip port probing and use this one. |
| `-PgUser` | `postgres` | Login role. Needs permission to create databases. |
| `-PgPassword` | prompted | Also read from `PGPASSWORD` when set. |
| `-PgBinPath` | auto | The `bin` directory of a PostgreSQL install, for layouts the search does not find. |
| `-Database` | `webhealth` | Database to create. |
| `-Seed` | `Snapshot` | `Snapshot` restores the development data. `Empty` builds the schema from migrations and leaves only the roles and the administrator. |
| `-AdminEmail` | `admin@example.test` | Administrator sign-in address. |
| `-AdminPassword` | `Hnjm1hnjm23_` | Administrator password. Applies only when the account is being created — see below. |
| `-EnableScheduling` | off | Enable live background monitoring, crawling, maintenance, PageSpeed audits, PNG image audits and notifications. |
| `-NoScheduling` | off | Explicitly keep background work disabled; retained for existing setup commands. |
| `-Force` | off | Replace an existing database without the confirmation prompt. |
| `-Run` | off | Start the application when setup finishes. |

Running the script a second time rebuilds the database from scratch. It is not incremental.

### About `-AdminPassword`

The snapshot already contains `admin@example.test` with a stored password. Bootstrapping only sets
a password when it *creates* an account, so with `-Seed Snapshot` the snapshot's password wins and
`-AdminPassword` has no effect. The script detects this and tells you, rather than printing a
password that would not work. To choose your own password, either use `-Seed Empty`, or pass a new
`-AdminEmail` so a second administrator is created.

### About scheduling

By default all seven background-work switches are off, so nothing runs in the background and the seeded
data stays exactly as it was captured. This is the predictable supervisor-demo mode.

Pass `-EnableScheduling` to monitor the four seeded websites and watch the system work. Every seeded
schedule may be overdue by the time you restore it, so the application can make outbound requests
and change the dashboard within a minute. On a network that blocks outbound traffic this can create
fresh "down" incidents that say more about the network than about the project.

## If something goes wrong

**"running scripts is disabled on this system"** — use `setup.cmd`, or
`powershell -ExecutionPolicy Bypass -File .\setup.ps1`.

**"No PostgreSQL server is listening"** — the service is not running, or it is on an unusual port.
Start it from `services.msc`, or pass `-PgPort`.

**"Could not authenticate"** — this is the password for the PostgreSQL `postgres` role chosen
during installation, not a Windows password.

**"PostgreSQL client tools were not found"** — PostgreSQL is installed somewhere the search does
not reach, such as a Docker container or WSL. Point at a local `bin` directory with `-PgBinPath`,
or install the Windows client tools.

**"pg_restore N cannot read a PostgreSQL 18 archive"** — no version 18 was found anywhere. Install
it, point `-PgBinPath` at it, or run `-Seed Empty`.

**"The database could not be created"** — if you are connecting through pgbouncer or another
connection pooler, connect to the PostgreSQL server directly instead.

**Browser warns about the certificate** — run `dotnet dev-certs https --trust` in a new terminal and
restart the browser. Firefox keeps its own certificate store and will warn regardless.

**Port 7144 already in use** — an instance is already running. Close it, or start on another port
with `dotnet run --project src\WebHealth.Web --no-launch-profile --urls https://localhost:7199`.

## Refreshing the snapshot

`setup/webhealth-seed.dump` is a point-in-time capture, so its timestamps age. To re-capture it from
the current development database before handing the project over:

```powershell
.\scripts\capture-seed-snapshot.ps1
```

Commit the result so a fresh clone seeds the current data.

## A note on the secrets in this repository

The connection string, the PageSpeed Insights key and the Gmail SMTP credentials are committed
deliberately. This is a personal internship project with no deployment, and the database is local.
Keeping them in the repository is what makes a single-command setup possible. A real deployment
would move all of it to a secret store.

The SMTP credential is a Gmail App Password, not the account password: the account has 2-Step
Verification enabled, and the app password is scoped to SMTP only and revocable on its own from
[Google Account app passwords](https://myaccount.google.com/apppasswords). Unlike the Mailgun
sandbox it replaced, it does grant send-as access to a real mailbox, so revoke and reissue it from
that page if the repository is ever shared more widely.

## Monitoring connection attempts

`Monitoring:HttpTransport:PerAddressConnectTimeout` defaults to `00:00:05` and accepts
`00:00:01` through `00:00:10`. Startup rejects values outside these bounds. Each DNS
address attempt has this limit, including waiting for its per-IP concurrency slot.
All DNS answers are validated before connection; permitted addresses are tried in resolver
order. The check timeout remains the overall deadline across DNS, attempts, TLS, and HTTP.
No database migration is required for this setting.

## Snapshot v2 and target permissions

The monitoring hardening upgrade adds `MonitoringSnapshotV2` and `TargetAuthorizationEvidence`.
Apply migrations explicitly before starting the upgraded application; startup does not apply them.
Existing completed snapshots remain historical. New checks capture their target and lifecycle
generation so queued evidence cannot overwrite health after configuration or lifecycle changes.

The upgrade creates no target permission automatically. An Administrator or Operations user must
open an endpoint, choose **Actions → Target permissions**, and record an ownership or explicit
permission reference for its host and port. Grant separate permission for any redirect destination
host/port. Permissions are scoped to that endpoint; they do not override prohibited-address rules.
Only one current permission can cover the same endpoint, host, and port. Revoke it before replacing
it. Revocation needs a reason and prevents subsequent connection attempts; an existing connection
may finish. An optional expiry includes its time zone, for example `2026-12-31T23:59:00Z`.

Permission references and revocation reasons stay out of snapshots and audit payloads. Do not
place credentials or secrets in them. Existing endpoints without evidence fail closed at connection
time. This is a deliberate local/demo upgrade step, not automatic permission for stored targets.

### Snapshot v2 rollback

Stop the application and workers before explicitly rolling back `MonitoringSnapshotV2`.
Rollback completes unfinished v2 checks as cancelled, releases their leases and work, and
reduces snapshots to the legacy v1 representation before removing target/generation fields.
Completed result facts survive, but the frozen target and superseded disposition do not;
rolling forward cannot recover those removed facts. The permission migration rollback also
removes permission evidence, so grants must be recorded again after reapplying it.
Use a database backup when preserving these new facts is required. Normal upgrades leave
completed v1 snapshots unchanged. The isolated database suite verifies populated rollback
and repeatable reapplication; application startup never performs migrations.

### HTTP policy v2

Apply `20260908120653_HttpMonitorOverridesV2` explicitly before using the new HTTP policy forms.
It canonicalizes active HTTP configuration without changing effective column values or existing
fingerprints. Existing 30-second timeouts become explicit overrides; new monitors and blank/reset
timeouts use 15 seconds. Historical intent cannot be recovered from old materialized values, so
migrated timeout, confirmation, and threshold values are shown as overrides. Interval inheritance
is retained when it agrees with the environment default.

Administrators retain exclusive control of interval changes. Registry managers can configure HTTP
timeout (1–120 seconds), failure/recovery counts (1–10), thresholds, up to 20 additional 300–499
statuses, a marker of at most 500 characters, and ordinal comparison. Warning must be at least 1 ms;
critical must be at least warning and no greater than timeout. Each blank threshold uses its default.
Transport redirect/body limits remain system settings. All 2xx statuses are accepted; 5xx and
redirect/security failures cannot be made healthy by an accepted status.

Legacy effective values outside the new bounds are preserved by migration, not silently adjusted.
Such policies must be corrected through Edit endpoint before checks can be created. An inconsistent
typed policy produces a `ConfigurationDrift` event containing only monitor/endpoint identifiers.
Manual runs explain the configuration problem; scheduled dispatch skips the affected monitor and
continues processing valid ones. Saving a valid policy repairs its materialized values and fingerprint.

Rollback removes HTTP marker/status overrides from current configuration and recomputes the legacy
fingerprint while retaining effective timeout/confirmation/threshold columns and historical snapshots.
Stop workers and the application first. Reapplying the migration cannot recover removed overrides;
retain a database backup when they must be preserved. No additional table or column is introduced.

### Certificate validation networking

HTTP monitoring and the SSL inspection probe disable certificate downloads and revocation checks.
Missing intermediates must be supplied by the server or already available locally; the monitor
will not fetch AIA, OCSP, or CRL URLs embedded in certificates. Revocation is not checked.
Normal HTTPS traffic still requires a valid certificate. The separate SSL probe records evidence
and rejects its handshake without sending application data.

The certificate-controlled-networking integration test needs OpenSSL. On Windows it uses the
copy bundled with Git for Windows (`Git/usr/bin/openssl.exe`); elsewhere it uses `openssl` on PATH.
Set `WEBHEALTH_TEST_OPENSSL` to an explicit executable path if needed. The fixture starts only a
loopback server, uses temporary test keys, and removes them when it stops. OpenSSL is a test
prerequisite, not an application runtime dependency. It avoids Windows test-server OCSP stapling
requests interfering with measurements of the monitoring client's network behavior.

### Equal HTTP response thresholds

Apply `HttpThresholdEquality` explicitly with the other pending migrations. It permits equal
warning and critical response thresholds in both monitor configuration and immutable snapshots,
as allowed by the HTTP policy contract. Its rollback deliberately retains the relaxed check
constraints: tightening them would invalidate saved policies and historical evidence. Rolling
back application code does not require rewriting those values. A future strict-schema conversion
would need its own explicit data-preservation plan.

### Structured certificate facts

Apply `StructuredCertificateFacts` explicitly before running this application version. It adds
leaf validity, hostname status, chain trust, and bounded JSON chain-status names. Historical
validity and hostname facts are derived from stored timestamps and matching results. Historical
positive chain-trust claims become Unknown because earlier chain-wide errors may not have been
recorded. Negative trust remains Untrusted. No historical chain codes or revocation results are
invented. Downgrading drops these new facts while retaining the older certificate fields; back up
the database before rollback if the structured evidence must be retained.

### SSL snapshot expiry thresholds

Apply `SslSnapshotExpiryPolicy` before running this version. New v2 SSL snapshots require strictly
ordered warning/high/critical day thresholds. Existing v2 SSL snapshots are backfilled with the
30/15/7 defaults used by their original execution path. The migration briefly disables only the
snapshot immutability trigger inside its transaction for that backfill. V1 snapshots retain an
explicit legacy-default fallback. Downgrading removes the recorded thresholds, so preserve a
backup if non-default historical expiry policy must remain explainable.

### SSL-specific policy fingerprints

Stop workers and apply `SslPolicyFingerprint` explicitly. It replaces active SSL fingerprints only
when the stored value matches the computed legacy hash for that target and materialized policy.
Unknown hashes are preserved for investigation. The new canonical hash includes expiry thresholds
as well as target URL, production status, cadence, timeout, and confirmations. This advances the
monitor generation; already queued legacy checks remain readable and finish as historical evidence
without overwriting current state. Rollback converts matching default-expiry hashes back to the
legacy representation and leaves immutable checks unchanged. Custom-expiry hashes cannot be
represented by the legacy format and are not rewritten.

### Monitoring freshness and dispatch delay

`Monitoring:Scheduling:DispatchDelayGrace` defaults to `00:10:00` and accepts two through thirty
minutes. Startup rejects values outside those bounds. Endpoint detail shows confirmed health and
operational state separately for availability and SSL monitors. Only a completed scheduled check
accepted as Current refreshes scheduled freshness; manual, urgent and superseded checks do not.
Freshness expires strictly after the interval plus the greater of ten minutes or one quarter of
the interval. A monitor is delayed strictly after its due time plus dispatch delay grace.
Lifecycle eligibility, manual-only mode and pause take precedence over delay or freshness.
No database migration is needed for this derived projection. Dashboard rows and CSV export use
the same operational-state rules. CSV appends `OperationalState` and `LastScheduledCompletionAt`;
`ConfirmedStatus` now retains health when checks stop. Health filters accept Healthy, Warning,
Critical and Unknown. Saved links using `HealthStatus=Disabled` must be changed because Disabled
is an operational state. Runtime diagnostics remain a subsequent part of increment 5.

### Scheduler runtime records

Apply `20260908135926_MonitoringRuntimeState` explicitly before starting the updated scheduler.
The migration creates an initially empty `monitoring_runtime_state` table. Dispatch and
reconciliation record their start and latest completion, including zero-work runs. Completion
stores duration, bounded failure category and consecutive failures; recovery resets the counter
while preserving the last failure time. An invocation identifier prevents an older overlapping
run from overwriting the newest invocation. No target URLs, worker IDs or exception messages are
stored here. If PostgreSQL is unavailable, runtime persistence is unavailable too; a safe log
records a failed completion write, and existing scheduler error/recovery behavior remains in force.
Rollback drops this operational history and leaves monitoring results untouched. The protected
runtime diagnostics UI is a subsequent part of increment 5.

### Protected monitoring diagnostics

Administrators and Operations can open `/Diagnostics/Monitoring` from the dashboard monitoring
system card. Registry filters scope aggregate monitor/work counts; scheduler and worker evidence
is global engine state. Viewer dashboard output receives only the coarse engine assessment and
its authorized aggregates, never the detailed runtime object. No worker IDs or exception text
are exposed. `/health/monitoring` uses the same diagnostics policy and returns 503 for Critical or
unknown engine health, 200 for Healthy or Warning. `/health/ready` remains a database readiness
check and does not depend on monitored target failures.

Scheduler success age warns at three minutes and is critical at five; three consecutive failures
are critical. Worker heartbeat tolerance is two minutes and a worker must cover the monitoring
queue. Queue age warns at five minutes and is critical at fifteen. Dispatch overdue warning uses
DispatchDelayGrace and critical starts at thirty minutes. Disabled scheduling reports Healthy
with DisabledByConfiguration while preserving historical scheduler evidence on the protected page.

### Local monitoring telemetry

The `WebHealth.Monitoring` meter publishes `monitoring.operations` and `monitoring.duration`
(milliseconds) for transport attempts, dispatch and reconciliation. These measure engine operations,
not confirmed target health. Dimensions are limited to `monitor_type`, `source`, `operation` and
`failure_category`; unexpected values become `Unknown`. No external exporter is required or enabled.
Execution log scopes carry check, work, endpoint, monitor, attempt, job, worker, source, snapshot
schema and generation identifiers. Transport warning events contain a safe category rather than
raw exception text, which could contain target data. No migration or configuration change is needed.

### Retention hold schema

Apply `20260908144110_RetentionHolds` explicitly with the normal migration procedure. It creates
the bounded hold-history table; the compiled model is updated with it. The Administrator management
flow and deletion worker are still being implemented, so this schema alone does not enforce holds.
No deletion worker is enabled. Rollback drops all hold history; keep deletion disabled during any
rollback. See `docs/phase-7/Retention_Runbook.md` for the policy and supported scopes.

The hold application service now validates Administrator access and existing scope records, with
atomic creation/release audits and idempotent release. It requires no additional migration. The
web management flow and retention worker are still pending; deletion remains disabled.

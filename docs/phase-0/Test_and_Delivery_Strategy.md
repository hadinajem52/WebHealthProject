# Testing, Controlled Targets, and Immediate Spikes

**Owner:** Intern  
**Status:** Phase 0 immediate spikes complete; later-phase tests are implemented incrementally
**Approval:** Approved by the intern/project owner on 2026-08-13

## 1. Test boundaries

- Unit: deterministic normalization, cadence, classifications, issue keys, counters, transitions, thresholds, uptime, and idempotency using xUnit/FluentAssertions.
- Web integration: `WebApplicationFactory` for role/assignment authorization, anti-forgery, validation, encoding, MVC/JSON/CSV, and safe errors.
- Database integration: disposable PostgreSQL/Testcontainers for migrations, uniqueness, transactions, `SKIP LOCKED`, leases, concurrency, and Hangfire storage.
- Outbound integration: controlled local HTTP/TLS/DNS/proxy targets; never public client websites.
- Email: recording fake only in automated tests.
- Time: injected `TimeProvider` for schedules, incidents, maintenance, reminders, and reporting boundaries.

## 2. Controlled targets

| Fixture | Phase 0 / implementation capability |
|---|---|
| HTTP | Statuses, delays, disconnects, relative/cross-host redirects, loops, large/chunked bodies, cancellation |
| DNS | Deterministic A/AAAA, mixed public/private, changing/rebinding answers |
| TLS | Valid, expired, future, mismatch, self-signed/untrusted certificates |
| Proxy | Fake proxy proving `UseProxy=false` prevents contact |
| PostgreSQL | Clean schema, competing transactions, restart/idempotency |
| Email | Success/transient/permanent recording fake; no network delivery |
| Later crawler site | Robots, sitemap, noindex, canonical, path/query scope and broken links; detailed in Phase 6 |

## 3. Supported-version gate

Before Phase 1 dependencies are finalized:

1. Pin a supported GA .NET 10 SDK in `global.json`.
2. Select mutually supported EF Core, Npgsql, Hangfire, PostgreSQL storage provider, and PostgreSQL versions from primary documentation/release notes.
3. Record package versions centrally or directly in projects and commit the relevant lock/version files.
4. Run restore, build, unit tests, and a real PostgreSQL integration test.
5. Record license and vulnerability checks for added packages.

Pinned and exercised on 2026-08-13: .NET SDK `10.0.400`, EF Core `10.0.11`, Npgsql/EF provider `10.0.3`, Hangfire Core `1.8.24`, Hangfire.PostgreSql `1.21.1`, and PostgreSQL `18`. Versions are centralized in [`../../Directory.Packages.props`](../../Directory.Packages.props), the SDK is pinned in [`../../global.json`](../../global.json), and [`../../tests/FeasibilitySpikes/packages.lock.json`](../../tests/FeasibilitySpikes/packages.lock.json) locks the resolved graph. The transitive `Newtonsoft.Json` version is pinned to `13.0.4` because the provider's lower dependency floor otherwise resolved to vulnerable `11.0.1`.

## 4. Immediate Phase 0 spikes

### SP-01 — dependency and Hangfire/PostgreSQL compatibility

Pass when pinned supported versions can create Hangfire schema, enqueue/execute work, persist across restart, and isolate queues against disposable PostgreSQL.

### SP-02 — safe connection enforcement

Pass when `SocketsHttpHandler.ConnectCallback` resolves through an injected resolver, selects a permitted IPv4/IPv6 address, connects directly, verifies the peer, preserves Host/SNI, and rejects mixed/prohibited answers.

### SP-03 — redirects, rebinding, TLS, and no-proxy

Pass when controlled fixtures prove every redirect is revalidated, loops/hop limits terminate, rebinding cannot contact the blocked listener, implicit proxy settings are ignored, and invalid TLS remains failed while bounded certificate evidence is captured.

### SP-04 — PostgreSQL concurrency invariants

Pass when competing transactions demonstrate one lease winner, fencing/expiry recovery, one logical result, one active matching incident, and one event/channel/recipient notification.

Record actual commands, versions, outcomes, limitations, and resulting design changes below as spikes run.

| Spike | Result | Evidence |
|---|---|---|
| SP-01 | Passed 2026-08-13 | [`PostgreSqlSpikeTests`](../../tests/FeasibilitySpikes/PostgreSqlSpikeTests.cs) created the Hangfire schema, executed persisted jobs after storage recreation, isolated `alpha`/`beta` queues, and connected through EF Core/Npgsql to PostgreSQL 18. |
| SP-02 | Passed 2026-08-13 | [`SafeHttpSpikeTests`](../../tests/FeasibilitySpikes/SafeHttpSpikeTests.cs) resolved inside `ConnectCallback`, rejected mixed answers, pinned a selected address, verified `RemoteEndPoint`, and preserved the original HTTP Host. |
| SP-03 | Passed 2026-08-13 | Controlled listeners proved per-hop redirect validation, loop/hop termination, blocked-listener rebinding prevention, `UseProxy=false`, and failed self-signed/mismatched TLS with a bounded certificate summary. |
| SP-04 | Passed 2026-08-13 | Competing Npgsql operations proved one lease winner, expiry takeover with incremented fencing generation, stale-generation rejection, and unique logical result, active incident, and notification delivery. |

### 4.1 Execution evidence

Environment: Windows 10 build `26200`, .NET SDK `10.0.400`, PostgreSQL `18` binaries, x64. Docker CLI `29.6.2` was installed but its daemon was unavailable, so the runner created a credential-free, repository-ignored PostgreSQL cluster on loopback port `6543` with `initdb`, started it for the tests, and stopped it in `finally`. It did not modify the separately running PostgreSQL Windows service.

Command from the repository root:

```powershell
./scripts/run-feasibility-spikes.ps1
```

Final result on 2026-08-13: restore succeeded without vulnerability warnings; the test assembly built without warnings; all 6 tests passed in 7.0 seconds; the temporary PostgreSQL cluster stopped successfully. Earlier fixture failures were corrected before this result and are not counted as passes.

Resulting decisions:

- Keep `SocketsHttpHandler.ConnectCallback`, direct IP connection, peer verification, manual redirects, disabled implicit proxy, and authoritative platform TLS validation in the foundation.
- Keep lease takeover conditional on expiry and increment the fencing generation atomically; require generation on final writes.
- Keep PostgreSQL partial/unique indexes as the final active-incident and idempotency defenses.
- Use the current Hangfire PostgreSQL connection-factory API. Review provider compatibility before any future `2.x` upgrade.
- Keep Testcontainers for Docker-capable CI. The isolated native cluster is the reproducible Windows fallback for Phase 0 evidence.

## 5. Basic CI

Use GitHub Actions or another available personal CI service. Minimum pull-request/main checks:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --logger "trx"
```

Also run formatting/static checks selected by the project, secret scanning, and dependency vulnerability/license checks. PostgreSQL integration jobs require a Docker-capable runner. If CI is not yet configured, the same commands run locally and the limitation is recorded; CI remains required by AC-15 before project completion.

## 6. Later-phase tests

- Phase 5: SSL boundaries, dashboard query plans, representative P95.
- Phase 6: SEO/crawler scope, rates, limits, cancellation, and isolation.
- Phase 7: 500 endpoints, 100 bounded checks, schedule coverage, retention, dependency failures, and restore if deployment is pursued.
- Production Gmail delivery, HA, PITR, production alerting, and deployment smoke tests are optional deployment work and are not Phase 0 blockers.

## 7. Evidence format

Record commit, environment, exact versions, command, outcome, failed assertions, sanitized logs, and known limitations. The intern reviews and signs off their own evidence; optional peer feedback can be linked but is not required.

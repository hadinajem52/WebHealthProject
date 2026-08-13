# Phase 1 Testing Foundation

## Scope

This foundation enables AC-15 and later business-rule tests without claiming that any Phase 2–7 workflow is implemented.

- Unit tests cover deterministic domain and application rules without ASP.NET Core, EF Core, network, or database dependencies.
- Web integration tests use a shared `WebHealthWebApplicationFactory` with isolated in-memory configuration.
- Docker-capable environments can run the migration assertions through PostgreSQL Testcontainers.
- PostgreSQL integration tests apply real migrations against a disposable local PostgreSQL 18 cluster.
- Database tests never use a developer database and automated tests never contact public websites or SMTP services.

No user-visible behavior, authorization policy, application data, or migration changed in this work item. Test configuration contains no secrets, and the PostgreSQL password used by the local harness is limited to its disposable test cluster.

## Repeatable commands

Run the normal foundation suite from the repository root:

```powershell
./scripts/run-tests.ps1
```

The script performs a locked restore, a warning-sensitive solution build, and all normal unit and integration tests. The PostgreSQL-only test is reported as skipped because it requires an isolated database.

Run the Testcontainers path when Docker is available:

```powershell
$env:WEBHEALTH_TESTCONTAINERS = 'true'
dotnet test tests/WebHealth.IntegrationTests/WebHealth.IntegrationTests.csproj --filter 'FullyQualifiedName~PostgreSqlTestcontainerTests'
Remove-Item Env:WEBHEALTH_TESTCONTAINERS
```

Run the real PostgreSQL migration test separately:

```powershell
./scripts/run-database-foundation-tests.ps1
```

This command creates a disposable native PostgreSQL 18 cluster under the ignored `.spikes` directory, applies the foundation migration, verifies the resulting schema, repeats the explicit migration update, and stops the cluster in a `finally` block.

## Conventions for later tests

- Name tests as `Member_Scenario_ExpectedResult`.
- Use xUnit theories for meaningful input boundaries.
- Use FluentAssertions 7 for readable multi-value and collection assertions; version 7 remains fully open source.
- Put reusable web-host and database helpers under `tests/WebHealth.IntegrationTests/Support`.
- Inject `TimeProvider` when production behavior depends on time.
- Add a regression test with every nontrivial business rule and confirmed defect.
- Keep Docker, database, controlled-network, and SMTP tests explicitly separated from the fast unit suite.

## Accepted Testcontainers advisory

On 2026-08-13, the intern explicitly accepted the transitive `SSH.NET` risk under `GHSA-q939-rpr3-3284` so the Testcontainers foundation could proceed. The integration-test project suppresses only that advisory through `NuGetAuditSuppress`; package auditing remains enabled for every other advisory. Re-evaluate and remove the suppression when a clean Testcontainers dependency graph is published. The native disposable PostgreSQL harness remains the verified Windows fallback.

## Current evidence

On 2026-08-13:

- locked restore completed;
- the solution built with zero warnings and zero errors;
- the unit project executed two architecture-boundary cases for Domain and Application;
- twenty-one normal integration tests passed and the isolated PostgreSQL test was skipped as designed; twelve of them are the application-shell cases added with the shell work item;
- the Testcontainers test compiled and was skipped because Docker Desktop was not running;
- the disposable PostgreSQL foundation test passed separately;
- restore emitted no unsuppressed audit warnings; an explicit vulnerability listing continues to report the accepted `SSH.NET` advisory.

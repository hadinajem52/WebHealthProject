# Phase 1 Delivery Checks

## Delivery contract

Phase 1 uses the same repository checks locally and in GitHub Actions. The checks support AC-15 and all later work; they do not deploy the application or mutate a database schema.

Run the local delivery gate from the repository root:

```powershell
./scripts/run-delivery-checks.ps1
```

The command verifies:

- the pinned `dotnet-ef` tool and locked NuGet dependency graph restore;
- source formatting has no pending changes;
- the solution builds in Release with analyzers and warnings treated as errors;
- the EF Core model has no changes missing from migrations;
- unit and normal integration tests pass and write TRX results under the ignored `TestResults` directory;
- no vulnerable package exists except the explicitly accepted SSH.NET advisory `GHSA-q939-rpr3-3284`;
- no simple credential-assignment pattern exists outside generated output and lock files;
- the Git diff contains no whitespace errors.

The migration drift check requires a syntactically valid design-time connection string but does not connect to PostgreSQL or apply migrations. Real schema application remains an explicit separate command:

```powershell
./scripts/run-database-foundation-tests.ps1
```

## Personal CI

`.github/workflows/delivery.yml` runs for pull requests, pushes to `main`, and manual dispatches. It uses the SDK pinned by `global.json`, restores locked packages, runs the delivery gate in Release, and enables the PostgreSQL Testcontainers case on the Docker-capable GitHub-hosted Linux runner. A separate Gitleaks job scans the complete Git history for committed secrets.

The workflow has read-only repository permissions, bounded timeouts, and concurrency cancellation. It does not receive application, database, SMTP, Figma, or deployment secrets.

## Repository conventions

`Directory.Build.props` enables deterministic builds, .NET analyzers, code-style enforcement, warnings-as-errors, and CI build metadata. `.editorconfig` records whitespace and indentation conventions. Generated migration encoding is not rewritten merely to normalize line endings.

## Accepted risk and limitations

The intern accepted the Testcontainers transitive SSH.NET advisory on 2026-08-13. The delivery audit allows only that package/advisory pair and fails for every new vulnerability. Remove both the NuGet suppression and delivery exception when a clean Testcontainers dependency graph is available.

The GitHub Actions workflow is repository evidence, but it is not marked as remotely passing until an actual GitHub run completes. The local delivery gate passed on 2026-08-13; the Testcontainers case remains locally skipped when Docker Desktop is unavailable.

# Phase 1 Database Conventions

## Foundation contract

- Application tables use the `web_health` PostgreSQL schema.
- EF migration history uses `web_health.__ef_migrations_history`.
- Table, column, key, foreign-key, and index identifiers use deterministic snake case.
- `DateTimeOffset` properties map to PostgreSQL `timestamp with time zone`; IANA timezone identifiers remain separate text values.
- Relationships use `DeleteBehavior.Restrict` by default.
- Nullable reference types and explicit entity configuration control `NULL` versus `NOT NULL`.
- Migrations remain in `WebHealth.Infrastructure` beside `ApplicationDbContext`.
- Database creation and migration are explicit operations; application startup never applies migrations.

The `InitialFoundation` migration intentionally contains no application tables. Applying it to a clean PostgreSQL database creates only the `web_health` schema and EF migration-history table. Phase 2 introduces the first business schema after the revised database design is re-approved.

## Entity configuration rules

Each entity receives a focused `IEntityTypeConfiguration<T>` in Infrastructure. Configuration must explicitly define relevant lengths, requiredness, check constraints, indexes, partial uniqueness, concurrency tokens, and delete behavior. Database constraints remain the final defense for correctness-critical rules.

Use application-generated `Guid` identifiers and UTC `DateTimeOffset` instants. Never store a timezone offset as a substitute for an IANA timezone identifier.

## EF tool setup

Restore the repository-pinned EF tool:

```powershell
dotnet tool restore
```

Set the design-time connection string only in the current process:

```powershell
$env:WEBHEALTH_MIGRATIONS_CONNECTION = "Host=localhost;Port=5432;Database=webhealth;Username=webhealth;Password=<local-password>"
```

Add a reviewed migration:

```powershell
dotnet ef migrations add <MigrationName> --project src/WebHealth.Infrastructure --startup-project src/WebHealth.Infrastructure --output-dir Persistence/Migrations
```

Apply reviewed migrations explicitly:

```powershell
dotnet ef database update --project src/WebHealth.Infrastructure --startup-project src/WebHealth.Infrastructure
```

Remove `WEBHEALTH_MIGRATIONS_CONNECTION` from the process after the command. Never commit it or place it in a command transcript containing real credentials.

## Verification

Run the clean PostgreSQL verification:

```powershell
./scripts/run-database-foundation-tests.ps1
```

The script creates a credential-free isolated PostgreSQL 18 cluster under the ignored `.spikes` directory, creates a clean database, executes the database integration test, reruns the explicit EF update to prove it is a no-op, and stops the cluster in `finally`.

Evidence recorded on 2026-08-13:

- The baseline migration applied successfully to a clean PostgreSQL 18 database.
- `web_health` existed with only `__ef_migrations_history`.
- Exactly one migration was recorded and no migration remained pending.
- A second explicit database update applied no migrations.
- The isolated cluster stopped successfully.
- The six Phase 0 feasibility spikes still passed after extracting the shared PostgreSQL test-cluster helper.

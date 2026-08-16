# Database and completion gate

## Reviewed migration boundary

`InitialFoundation` remains the Phase 1 baseline and contains no application tables. Phase 2 is represented by two reviewable migrations:

1. `IdentityAccessAndAudit` creates ASP.NET Core Identity, teams, effective membership, owner subjects, append-only audit storage, indexes, and the non-overlap constraint.
2. `RegistryFoundation` creates clients, websites, environments, endpoints, tags, scoped grants, target-authorization evidence, monitor/policy foundations, normalized uniqueness constraints, restrictive foreign keys, concurrency fields, and deferred cross-table invariants.

Registry-scoped access grants are in `RegistryFoundation` because their foreign keys require the registry scope tables.

The former incremental Phase 2 migrations were development-only and are replaced by this consolidated pair. A local database that already applied the former Phase 2 chain must be reset to the Phase 1 baseline or recreated; this pre-release consolidation is not an in-place upgrade from those discarded migration identifiers.

## Migration validation

The isolated PostgreSQL 18 gate verifies:

- current migrations applied directly to a completely clean database;
- downgrade to `InitialFoundation`, followed by upgrade through both Phase 2 migrations;
- a repeated migration application leaves the three migration identifiers unchanged;
- the resulting schema and required tables match the EF model;
- `dotnet ef migrations has-pending-model-changes` reports no drift;
- a second explicit `dotnet ef database update` applies no migrations.

## Completion evidence

| Requirement | Evidence | Status |
|---|---|---|
| Name and URL normalization boundaries | `NameNormalizerTests`, `TagNormalizerTests`, `EndpointUrlNormalizerTests` | Passed |
| PostgreSQL duplicate constraints | Native direct constraint assertions for Client, Website, Environment, Endpoint, Tag, and related invariants | Passed |
| Role/action direct-request matrix | `AuthorizationBaselineTests` | Passed |
| Assignment-scoped reads and writes | Native owner/grant/team read assertions plus assigned Developer write denial | Passed |
| Anti-forgery rejection | `AuthenticationShellTests` | Passed |
| Disabled-session invalidation | Native Identity security-stamp assertions | Passed |
| Stale update rejection | Native service assertions and `RegistryConcurrencyResponseTests` | Passed |
| Soft-delete and restore conflicts | Native archive visibility, conflict, cleanup, and restore assertions | Passed |
| Website enablement without an environment | Service and deferred PostgreSQL constraint assertions | Passed |
| Production HTTP exception authorization | Service and direct PostgreSQL transition assertions | Passed |
| Audit contents and sensitive-value exclusion | Native action/snapshot/search/append-only assertions and sensitive canaries | Passed |
| Output encoding | `RegistryLabelsTagsAndNotes_AreHtmlEncoded` | Passed |
| Full delivery workflow with Testcontainers | Docker Desktop 29.6.2 started, but Docker Hub and ECR image requests timed out during TLS negotiation before PostgreSQL 18 could be pulled | Blocked externally |

The equivalent repository-native disposable PostgreSQL 18 workflow passed. The Phase 2 completion gate remains open only for a successful Testcontainers rerun after container-registry connectivity is available:

```powershell
./scripts/run-delivery-checks.ps1 -UseTestcontainers
```

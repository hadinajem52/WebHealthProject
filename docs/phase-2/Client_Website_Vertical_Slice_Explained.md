# Phase 2.4: Client and Website Vertical Slice Explained

This guide explains the Client and Website registry implementation and connects
it to the authentication, authorization, assignment, and audit work from the
previous phases.

## The main idea

Phase 2.3 created the reusable assignment foundation:

- users and teams can be owners;
- team membership can be effective at a particular time;
- owner subjects provide one reusable owner reference;
- changes can be audited.

Phase 2.4 finally gives that foundation real registry records to protect:

It also introduces the scoped access-grant persistence needed for Viewer reads.

```mermaid
flowchart TD
    A[Client] --> B[Website]
    B --> C[Environment foundation]
    A --> D[Client owner_subject]
    B --> E[Website owner_subject]
    U[User or team assignment] --> D
    U --> E
    G[User access grant] --> A
    G --> B
    G --> C
```

The result is a working vertical slice: data model, database constraints,
application services, authorization, query filtering, MVC pages, lifecycle
operations, and audit events are connected together.

## What was delivered

The implementation now supports:

1. Client list, details, create, edit, disable, soft-delete, and restore flows.
2. Website list, details, create, edit, disable, soft-delete, and restore flows.
3. A minimal Environment table used to validate website enablement.
4. Owner selection using the reusable user/team owner subjects.
5. Registry read policy for all four application roles.
6. Registry management policy for Administrator and Operations.
7. Developer/Support visibility through direct or team ownership.
8. Viewer visibility through active scoped access grants.
9. Client ownership flowing down to its websites.
10. Website ownership controlling website visibility independently.
11. Name normalization and PostgreSQL uniqueness constraints.
12. Optimistic concurrency using a version number.
13. Transactional lifecycle changes with typed audit snapshots.

## How the phases connect

```mermaid
flowchart TD
    P0[Phase 0<br/>Database and security foundation]
    P1[Phase 1 / Task 2.1<br/>Identity and protected shell]
    P2[Phase 2.2<br/>Users, roles, global policies]
    P3[Phase 2.3<br/>Teams, owner subjects, effective membership, audit]
    P4[Phase 2.4<br/>Client and Website registry slice]
    P5[Later<br/>Environment and Endpoint management]

    P0 --> P1 --> P2 --> P3 --> P4 --> P5
```

### Phase 0 supplied the safety foundation

Phase 0 established the conventions used here:

- PostgreSQL and the `web_health` schema;
- explicit migrations instead of automatic startup migration;
- normalized names;
- restrictive foreign keys;
- optimistic concurrency;
- bounded and validated input;
- database constraints as the final correctness boundary.

### Phase 1 supplied identity

Phase 1 added:

- `ApplicationUser` and `ApplicationRole`;
- Identity password hashing and cookies;
- the bootstrap Administrator;
- protected MVC pages;
- disabled-user handling.

### Phase 2.2 supplied global authorization

Phase 2.2 added:

- Administrator, Operations, Developer/Support, and Viewer roles;
- user and team administration;
- global authorization policies;
- antiforgery protection;
- role-aware navigation.

### Phase 2.3 supplied ownership and audit

Phase 2.3 added:

- teams and effective membership;
- owner subjects that point to one user or one team;
- assignment evaluation;
- durable append-only audit events.

Phase 2.4 uses all of those pieces to decide which registry rows a user can see
and which registry changes a user can make.

## The registry hierarchy

The current hierarchy is:

```mermaid
flowchart TD
    C[Client<br/>customer or organization] --> W[Website<br/>site belonging to client]
    W --> E[Environment foundation<br/>Production, Staging, Development]
```

Example:

```text
Client:      Acme Corporation
Website:     acme.com
Environment: Production
Base URL:    https://acme.com
```

The Client and Website pages are available now. Environment persistence exists
to support the website enablement rule, but Environment CRUD is intentionally
deferred.

Endpoint records are also deferred. Therefore endpoint-level grants and endpoint
owner overrides are not part of this vertical slice.

## Where the code lives

### Application contracts

```text
src/WebHealth.Application/Registry/
├── RegistryContracts.cs
├── IClientRegistryService.cs
├── IWebsiteRegistryService.cs
└── IRegistryReader.cs
```

These contracts separate web requests from the registry business operations.

Important contracts include:

- `IRegistryReader`: filtered reads for clients and websites;
- `IClientRegistryService`: client mutations;
- `IWebsiteRegistryService`: website mutations;
- `RegistryAccessContext`: the current user ID and roles used by the service.

### Infrastructure implementation

```text
src/WebHealth.Infrastructure/Registry/
├── RegistryEntities.cs
├── RegistryReader.cs
├── RegistryVisibility.cs
├── ClientRegistryService.cs
├── WebsiteRegistryService.cs
├── RegistryMutationSupport.cs
└── RegistryEntityConfigurations.cs
```

### Web implementation

```text
src/WebHealth.Web/
├── Controllers/RegistryController.cs
├── Models/RegistryViewModels.cs
└── Views/Registry/
    ├── Clients.cshtml
    ├── Client.cshtml
    ├── CreateClient.cshtml
    ├── EditClient.cshtml
    ├── Websites.cshtml
    ├── Website.cshtml
    ├── CreateWebsite.cshtml
    └── EditWebsite.cshtml
```

## Registry authorization

Two new policies are registered in `Program.cs`:

| Policy | Roles | Purpose |
|---|---|---|
| `ReadRegistry` | Administrator, Operations, Developer/Support, Viewer | Open registry pages and read allowed rows |
| `ManageRegistry` | Administrator, Operations | Create, edit, disable, delete, and restore registry rows |

The controller uses the read policy at class level:

```csharp
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
```

Mutation actions add the stronger policy:

```csharp
[Authorize(Policy = AuthorizationPolicies.ManageRegistry)]
```

This creates two layers:

```mermaid
flowchart TD
    A[Request to RegistryController] --> B{Signed in?}
    B -->|No| C[Login challenge]
    B -->|Yes| D[ReadRegistry policy]
    D -->|Role missing| E[Forbidden]
    D -->|Role allowed| F[Read filtered rows]
    F --> G{Mutation action?}
    G -->|No| H[Return visible records]
    G -->|Yes| I[ManageRegistry policy]
    I -->|Administrator or Operations| J[Mutation service]
    I -->|Developer/Support or Viewer| E
```

The service checks management access again. The controller policy protects the
HTTP route, while the service check protects the application operation if it is
called from another entry point.

## What each role can see

The current global baseline is:

```mermaid
flowchart LR
    subgraph Roles
        A[Administrator]
        O[Operations]
        D[Developer/Support]
        V[Viewer]
    end
    subgraph Registry behavior
        G[Global read and manage]
        DA[Assigned-owner read]
        GR[Granted read]
        M[No registry manage]
    end
    A --> G
    O --> G
    D --> DA
    D --> M
    V --> GR
    V --> M
```

More precisely:

- **Administrator** and **Operations** can read all registry rows and manage them.
- **Developer/Support** can read records owned by the user or an effective team
  membership.
- **Viewer** can read records covered by an active access grant.
- **Developer/Support** and **Viewer** cannot use registry mutation actions.

Hiding a navigation link is only a usability improvement. The policies and
query filters remain the real security boundaries.

## Assignment-aware query filtering

`RegistryReader` does not load every row and filter it in the browser. It applies
visibility rules to the database query before the results are returned.

```mermaid
flowchart TD
    A[RegistryAccessContext] --> B[RegistryVisibility]
    B --> C{Administrator or Operations?}
    C -->|Yes| D[Global query scope]
    C -->|No| E{Developer/Support?}
    E -->|Yes| F[Owner subject assigned to user]
    C -->|No| G{Viewer?}
    G -->|Yes| H[Active Read/Manage grant for user]
    F --> I[Filtered database query]
    H --> I
    D --> I
    I --> J[Only visible clients/websites returned]
```

The current evaluator checks:

- the user is enabled;
- direct user ownership;
- effective team membership;
- the team is enabled;
- the membership is active at the current time.

The grant filter checks:

- the grant belongs to the current user;
- the grant has started;
- it has not expired;
- it has not been revoked;
- the user is enabled.

## Ownership inheritance

Ownership is stored independently on both Client and Website records.

For Developer/Support visibility, ownership flows like this:

```mermaid
flowchart TD
    A[User or effective team assignment] --> B[Client owner subject]
    A --> C[Website owner subject]
    B --> D[Client visible]
    B --> E[Website under that client visible]
    C --> F[Website visible]
```

Therefore a Developer/Support user can see a website when either:

- the website is owned by the user or an assigned team; or
- the containing Client is owned by the user or an assigned team.

For Viewer grants, visibility can flow from:

```mermaid
flowchart LR
    A[Client grant] --> B[Client and its websites]
    C[Website grant] --> D[That website]
    E[Environment grant] --> F[Containing website and client path]
```

Endpoint inheritance and endpoint overrides are deferred until an Endpoint
entity exists.

## Access grants

The `access_grant` table represents a user’s scoped access. Each row must point to
exactly one scope:

```mermaid
flowchart TD
    A[AccessGrant] --> B{Exactly one scope}
    B --> C[Client]
    B --> D[Website]
    B --> E[Environment]
```

The database allows only these access levels:

```text
Read
Manage
```

It also validates that an expiry, when present, occurs after the grant starts.
The current vertical slice uses active grants to scope Viewer reads. A complete
grant-management UI and resource-specific Manage enforcement belong to later
work; global registry management remains controlled by the Administrator and
Operations policy.

## Client lifecycle

Administrators and Operations users can:

```mermaid
flowchart LR
    A[Create client] --> B[Edit client]
    B --> C[Disable client]
    C --> D[Soft-delete client]
    D --> E[Restore client]
    E --> F[Disabled restored client]
```

Deletion is soft deletion. The row remains in the database with `DeletedAt` and
`DeletedByUserId`, preserving relationships and audit history.

Normal operational lists exclude deleted rows. Administrator and Operations use
the separate Archived registry view to review and restore them; assignment-scoped
roles cannot query the archive.

An active client name must be unique after normalization. Deleted names may be
reused because the unique index applies only while `deleted_at IS NULL`.

## Website lifecycle and environment rule

Websites have a parent Client and their own owner subject.

```mermaid
flowchart TD
    A[Create website] --> B[Website starts disabled]
    B --> C{Active non-deleted environment exists?}
    C -->|No| D[Cannot enable]
    C -->|Yes| E[Can enable website]
```

The same invariant is enforced twice:

1. application service validation gives a useful error message;
2. PostgreSQL deferred constraint triggers protect the database at commit time.

This prevents a website from being enabled without an active Environment and
prevents the final active Environment from being removed while the Website is
enabled.

The current UI creates and edits Clients and Websites. Environment CRUD is
intentionally deferred, so enabling a website currently requires Environment
data to be created through the supported database/test setup.

## Normalization and uniqueness

Names are normalized before comparison:

```mermaid
flowchart LR
    A["  Acme   Portal  "] --> B[Trim and normalize whitespace]
    B --> C[Unicode and case normalization]
    C --> D[Stored display name: Acme Portal]
    C --> E[Stored comparison name]
```

The database provides the final duplicate defense:

| Record | Uniqueness rule |
|---|---|
| Client | Normalized name is globally unique while not deleted |
| Website | Normalized name is unique within a Client while not deleted |
| Environment | Normalized name is unique within a Website while not deleted |

The same normalization helper is shared with earlier team and identity-related
rules instead of reimplementing string comparison in each service.

## Optimistic concurrency

Every Client and Website has a `Version` value. The edit form sends the version
that was displayed to the user.

```mermaid
sequenceDiagram
    participant A as Admin A
    participant B as Admin B
    participant DB as Database

    A->>DB: Open Client version 1
    B->>DB: Open Client version 1
    A->>DB: Save where version = 1
    DB-->>A: Save version 2
    B->>DB: Save where version = 1
    DB-->>B: Concurrency conflict
```

The second update does not silently overwrite the first update. The web layer
returns a safe reload message and preserves the stale version token, so submitting
the unchanged stale form again cannot overwrite the newer database state.

## Audit integration

Every successful Client and Website mutation writes a typed, allow-listed audit
snapshot in the same transaction as the state change.

```mermaid
sequenceDiagram
    participant User as Administrator or Operations
    participant Service as Registry service
    participant Audit as AuditTrailWriter
    participant DB as PostgreSQL

    User->>Service: Create, edit, disable, delete, or restore
    Service->>Service: Validate and change entity
    Service->>Audit: Write typed before/after snapshot
    Audit->>DB: Insert audit_event
    Service->>DB: Commit entity and audit together
```

Client snapshots contain:

- Client ID;
- name;
- owner subject ID;
- active state;
- deleted state;
- version.

Website snapshots contain:

- Website ID;
- Client ID;
- name;
- owner subject ID;
- technology/CMS;
- enabled state;
- deleted state;
- version.

Client note contents are deliberately excluded from audit snapshots. A safe
`NotesChanged` flag still records whether a mutation changed them. Passwords,
tokens, and arbitrary request data are also excluded.

Actions include:

```text
client.created
client.updated
client.disabled
client.deleted
client.restored

website.created
website.updated
website.disabled
website.deleted
website.restored
```

## Database migration

The explicit migration is:

```text
20260814110940_ClientWebsiteVerticalSlice
```

It adds:

```text
web_health.client
web_health.website
web_health.environment
web_health.access_grant
```

It also adds foreign keys, indexes, uniqueness rules, access-grant checks, and
the deferred website/environment constraint triggers.

The application does not apply migrations automatically at startup. The migration
was applied explicitly to the disposable native PostgreSQL verification database;
development databases must be updated explicitly when the change is adopted.

## Verification evidence

The implementation tests verify:

- all five current migrations apply with no pending migrations;
- Clients and Websites can be created and queried;
- duplicate names are rejected after normalization;
- Developer/Support ownership filtering works;
- Viewer client, website, and environment grants scope results;
- disabled owners are rejected for new mutations;
- stale versions return concurrency conflicts;
- website enablement requires an active Environment;
- soft-delete, restore, and disable lifecycle actions work;
- Client and Website audit actions are recorded;
- audit snapshots contain only approved fields.

Authorization tests verify:

| Role | Registry read | Registry manage |
|---|---:|---:|
| Administrator | Allowed | Allowed |
| Operations | Allowed | Allowed |
| Developer/Support | Allowed, assignment-scoped | Forbidden |
| Viewer | Allowed, grant-scoped | Forbidden |

## What is intentionally deferred

This vertical slice does not yet include:

1. Environment CRUD screens and services.
2. Endpoint records and Endpoint CRUD.
3. Endpoint-level grants.
4. Website endpoint-owner override.
5. Grant administration UI.
6. Full resource-specific `Manage` grant enforcement.
7. Monitoring checks and endpoint configuration.
8. Registry-specific assignment screens beyond owner selection.

## The complete Phase 2 mental model

When a future monitoring request needs to decide whether a user can access a
resource, trace it through this sequence:

```mermaid
flowchart TD
    A[HTTP request] --> B[Phase 1 authentication]
    B --> C[Phase 2.2 global role policy]
    C --> D[Registry Read or Manage policy]
    D --> E[RegistryVisibility query scope]
    E --> F[Owner subject and effective team membership]
    E --> G[Active scoped access grants]
    F --> H[Visible Client or Website rows]
    G --> H
    H --> I[Registry service or reader]
    I --> J[Audited result]
```

In plain language:

```text
Phase 1 identifies the user.
Phase 2.2 checks the user’s global role.
Phase 2.3 supplies ownership, teams, membership, and audit history.
Phase 2.4 applies those rules to real Client and Website records.
The next increment adds Environments and Endpoints to complete the registry.
```

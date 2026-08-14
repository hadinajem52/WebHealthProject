# Tags, lifecycle and audit UI

## Scope and rules

This increment completes BR-W07, BR-W10, and the registry portion of BR-A06/AC-13.

- Website tags are entered as comma-separated labels, trimmed with the shared name normalizer, compared case-insensitively, and de-duplicated before persistence.
- A website may have at most 20 tags and each tag is limited to 100 characters.
- Tags are global records. `website_tag` provides the many-to-many assignment and restrictive foreign keys preserve historical references.
- The database unique index on normalized tag name and normalization version is the final duplicate defense.
- Concurrent first use of the same global tag converges through `ON CONFLICT DO NOTHING`; each transaction then attaches the winning tag record instead of failing the website save.
- Website reads apply role/assignment scope before tag filters and counts are evaluated.
- Tag changes participate in the website transaction, optimistic concurrency version, and typed safe audit snapshot.
- Archive actions remain soft deletes. Active lists exclude archived records; restore returns configuration disabled.

## User-visible behavior and authorization

- Administrator and Operations users can assign tags in Website create/edit forms.
- All roles with registry-read authorization can see tags and filter only within their already-authorized website scope.
- Lifecycle controls use archive/restore language and explain that history is retained.
- Responsive tables retain their header semantics for assistive technology and provide a visible mobile label for every value and action cell.
- Administrator and Operations audit users receive controlled action choices, known entity-type suggestions, safe before/after values, UTC timestamps, actors, outcomes, and pagination.
- Razor output encoding remains the boundary for labels and audit values.

## Data and migration

The consolidated `RegistryFoundation` migration adds:

- `tag`, including display/normalized names, normalization version, creator, timestamp, and concurrency version;
- `website_tag`, with a composite primary key and restrictive Website, Tag, and actor foreign keys;
- the unique `(normalized_name, normalization_version)` tag index and lookup indexes.

## Verification evidence

- `TagNormalizerTests` covers trimming, normalization, ordering, splitting, and de-duplication.
- Native PostgreSQL integration checks prove repeated and concurrent input is stored once, scoped tag filtering/counts work, the unique index rejects a direct duplicate, and website audit snapshots contain the safe tag labels.
- The normal build and delivery suite cover controller binding, authorization, Razor compilation, migrations, and existing lifecycle regression behavior.

Actual monitor scheduling, HTTP checks, and check history remain Phase 3 work.

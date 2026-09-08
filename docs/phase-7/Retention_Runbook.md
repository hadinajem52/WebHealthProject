# Retention policy and implementation record

Increment 6 is in progress. No retention worker is enabled yet.

The implementation follows section 10 of the monitoring plan: raw monitoring, execution, crawl
and PageAudit history defaults to 90 days; certificate observations, daily aggregates and terminal
incident bundles default to 24 months. Current evidence, active incidents, active holds and required
comparison baselines take precedence over age.

Audit events have no age-based deletion in this local/demo implementation. Keeping them indefinitely
ensures they are never shorter-lived than the retained history they explain. A future audit retention
limit requires an explicit policy change and reference analysis; it is not implied by raw retention.

Repeated confirmed failures without a transition or severity increase do not add incident evidence,
timeline entries or audit mutations. Raw check history continues to capture each check. Opening,
severity escalation, recovery start, recovery interruption, resolution and certificate replacement
continue to create evidence. This bounds evidence growth for a steady incident without losing its
material transitions. Historical evidence is preserved; this change does not remove existing rows.

The worker will default to disabled and dry-run, with batches of 1,000, at most 20 batches per run,
a 30-second run budget and an hourly schedule. Holds and retention configuration are Administrator
operations. Dry-run evidence must be recorded before enabling deletion.

# PageSpeed Incident Policy

## Work item

**Rules:** PSI-INC-01 through PSI-INC-08  
**Acceptance criteria:** PSI-INC-AC01 through PSI-INC-AC12  
**Status:** Implemented for local and demo use

## User-visible behavior and authorization

- PSI-INC-01: Administrators can open **Administration → PageSpeed settings**.
- PSI-INC-02: A master switch enables or disables PageSpeed incident activity.
- PSI-INC-03: Performance, Accessibility, Best Practices, and SEO each have an independent minimum score from 0 through 100.
- PSI-INC-04: First Contentful Paint, Largest Contentful Paint, Total Blocking Time, Cumulative Layout Shift, and Speed Index each have an optional maximum value.
- PSI-INC-05: A completed scheduled mobile or desktop audit opens an incident when an enabled rule is breached.
- PSI-INC-06: A later completed scheduled audit within the rule resolves its incident.
- PSI-INC-07: Manual audits and provider failures never open or resolve incidents.
- PSI-INC-08: Disabling the master switch or one rule prevents new activity and leaves existing incidents unchanged.

The settings page is administrator-only. Operations, Developer/Support, Viewer, and anonymous direct requests are rejected by server-side authorization. The POST requires an anti-forgery token.

## Inputs, outputs, validation, and errors

Category minimums accept 0–100. Paint, blocking, and Speed Index maximums accept 0–600,000 milliseconds. Cumulative Layout Shift accepts 0–10 with three decimal places. A value equal to its threshold is healthy; category scores breach below the minimum and performance metrics breach above the maximum.

Settings use optimistic concurrency. If another request saves first, the submitted update is rejected and the latest values are shown. Policy changes produce a typed audit event. Invalid values are shown as validation errors and are not persisted.

## Incident behavior

PageSpeed owns one non-scheduled `PageSpeedInsights` incident monitor per configured endpoint. It does not share availability counters and is ignored by the HTTP scheduler. Issue keys identify the category or metric and mobile or desktop strategy. Threshold values are not included in issue keys, so editing a threshold does not create a second identity for the same rule.

One breached scheduled measurement confirms an issue and one measured pass confirms recovery. Category results below 50 and metric results in Lighthouse's poor band are Critical; other breaches are Warning. Maintenance suppression uses the existing incident notification behavior.

Notification subjects and bodies describe a PageSpeed threshold breach or recovery rather than claiming the endpoint is down.

## Data and migration impact

Migration `20260824081054_PageAuditIncidentPoliciesAndBatches` adds:

- the singleton `page_audit_incident_policy` table;
- `numeric_value` and `numeric_unit` on `page_audit_item`;
- `batch_id` on `page_audit_run`;
- `page_audit_run_id` on `incident_evidence`;
- the system PageSpeed incident policy profile;
- dedicated PageSpeed monitors for endpoints that already have PageAudit targets.

The incident-evidence source constraint requires exactly one system evidence source for automatic opening, failure, recovery, and resolution evidence. The migration `Down` removes PageSpeed incident dependants before removing their monitor profile.

## Security and privacy

Only normalized scores, numeric metrics, rule identifiers, thresholds, and bounded evidence snapshots are stored. Raw provider JSON, screenshots, traces, HTML, API keys, and provider error bodies are not incident evidence. Provider failures remain diagnostics and cannot become endpoint incidents.

## Tests and evidence

- Evaluator unit tests cover category boundaries, metric breaches, missing metrics, disabled rules, severity, and validation.
- Authorization integration tests cover every persona, anonymous access, and anti-forgery enforcement.
- The PostgreSQL foundation suite proves a scheduled breached score opens one incident with PageAudit-run evidence and a later passing score resolves it.
- The schema suite covers the migration, compiled model, upgrade path, evidence foreign keys, and endpoint purge behavior.
- Release build, unit tests, focused authorization tests, JavaScript tests, and the database foundation suite are the local delivery evidence.

## Logging and operations

Existing PageAudit completion logs remain the source signal for audit status, item counts, failed audit counts, Lighthouse version, and attempt number. A missing dedicated monitor is logged as a warning and skips incident evaluation without changing existing incidents. Policy mutations are visible in the application audit trail.

## Compatibility and rollout

Apply the migration explicitly before running the updated application. Existing PageAudit history remains readable; older items have null numeric values and cannot evaluate a metric rule until a new scheduled audit records that metric. The master incident switch defaults off, so applying the migration alone cannot create incidents.

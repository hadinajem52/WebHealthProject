# Minimum maintenance behavior

**Work item:** Phase 4.2  
**Rules:** BR-M01, BR-M02; BR-M03/M04 data and policy inputs  
**Acceptance contribution:** AC-09

## Delivered behavior

- Administrators and Operations users can list, create, inspect, replace, and cancel one-off maintenance windows.
- A window has one active client, website, environment, endpoint, or monitor scope; a UTC `[start, end)` occurrence; an IANA timezone label; a bounded reason; and notification/escalation/failure-counter policy values.
- Occurrences are immutable. Editing cancels the old window and creates a replacement, retaining historical result links.
- The active-maintenance evaluator resolves client-to-monitor scope and marks a final check result with its governing occurrence.
- A maintenance-marked scheduled result is excluded from contractual uptime. Checks still execute and remain visible.
- Maintenance mutations record typed, allow-listed audit events.
- Pages use the protected Operations policy, normal MVC anti-forgery protection, and application output encoding.

## Deferred integration

The evaluator exposes the suppression and escalation/failure-counter policy values now. Phase 4.4/4.5 will consume them when it atomically creates incident and notification records. That is when a durable `Suppressed` notification delivery record and escalation-pause accounting become meaningful; no notification table or delivery worker exists yet.

## Verification

- `MaintenanceIntervalTests` proves start-inclusive/end-exclusive and adjacent-window boundaries.
- The native PostgreSQL foundation gate proves service create, audit event, active resolution, cancellation, and inactive resolution, alongside the phase-4.1 schema constraints.
- Logical-check finalization now persists maintenance occurrence evidence and excludes maintenance-classified scheduled checks from uptime.

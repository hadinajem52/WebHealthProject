# UI Direction — Metronic 8 Demo 34

**Decision:** Use Metronic 8 Demo 34 as the application-shell and visual baseline for the server-rendered ASP.NET Core MVC application.  
**Decision source:** Project direction received 2026-08-13.  
**Implementation status:** Fully licensed, source-verified, and exact-version recorded; no Metronic blocker remains.
**Approval:** Approved by the intern/project owner on 2026-08-13

## 1. Superseded direction

This decision supersedes references to an unavailable Figma design and to a custom-vanilla-CSS-only UI. It is compatible with the business specification's recommended Bootstrap/Metronic baseline.

It does not supersede semantic HTML, responsive behavior, NFR-09 accessibility, server-side authorization, anti-forgery, output encoding, performance, or safe error handling.

## 2. License, source, version, and asset gate

The public preview at <https://preview.keenthemes.com/metronic8/demo34/> is a visual reference only. It currently presents the legacy Bootstrap Demo 34 and links to a v8.3.3 changelog, but only the acquired licensed artifact can define the implementation version.

Before assets enter the repository:

- [x] Confirm the intern owns a Metronic license that covers this personal application, source repository, and any demo deployment.
- [x] Record license provenance and redistribution restrictions without committing license keys or purchase credentials.
- [x] Obtain the official source package or supported ASP.NET Core starter kit.
- [x] Verify that the package contains or supports Bootstrap Demo 34.
- [x] Pin the exact package and record its integrity hash/provenance.
- [x] Inventory Bootstrap, KeenThemes components, icons, fonts, charts, and plugins with exact versions.
- [x] Review third-party notices, maintenance status, and security advisories.
- [x] Decide where proprietary source/generated assets may be stored; do not publish them if the license forbids it.
- [x] Define the controlled update and reproducible bundle-build process.

Do not scrape, hotlink, or copy assets from the public preview.

## 3. MVC integration

- Razor views and thin MVC controllers remain the presentation model.
- Keep MVC Tag Helpers, model binding, server validation, anti-forgery tokens, and server-side authorization.
- Host licensed assets locally with cache-busting fingerprints.
- Do not patch licensed vendor source. Permit reproducible, documented application bundles generated from the pinned source package; put small application overrides after vendor styles.
- Add shared layouts/partials for the app shell, authentication, navigation, breadcrumbs, page toolbar, flash messages, validation, statuses, filters, tables, pagination, and empty states.
- Use JavaScript as progressive enhancement for menus, drawers, dialogs, tabs, and charts. Core navigation and forms must still have server behavior.
- Do not ship the scaffold Bootstrap bundle beside another Bootstrap version from Metronic.
- Retain Chart.js unless the licensed package creates a concrete reason to change it.

## 4. Information architecture

Primary navigation:

1. Dashboard.
2. Registry: Clients, Websites, Environments, Endpoints.
3. Incidents.
4. Reports.
5. Administration: Users, Roles, Settings, Audit, Diagnostics; shown only when authorized.

The navigation is a convenience, not an authorization boundary. Direct requests must enforce the same permissions.

## 5. Responsive textual wireframes

### Login

- Dedicated authentication shell without operational navigation.
- Product identity, email, password, remember option if approved, submit, forgot-password path.
- Validation summary precedes fields; each field has a persistent label and associated message.
- Explicit lockout, disabled-account, invalid-credentials, and service-unavailable states without account enumeration.

### Dashboard

- Page title, selected filters, and as-of time.
- Summary cards for monitored, healthy, warning, critical, unknown, and maintenance counts; every card has text and icon, not color alone.
- Current-health table with client/site/environment, owner, response time, SSL, incident, and status.
- Uptime and response-time charts with equivalent text/table summaries.
- Open incidents, SSL expiry, broken links, and protected diagnostics sections.
- On narrow screens, filters become an accessible drawer and cards stack; tables scroll with sticky/visible headers and no hidden critical action.

### Registry

- Hierarchy breadcrumbs, search, status/owner filters, result count, create action when authorized.
- Paginated table; row actions have descriptive accessible names.
- Create/edit forms group identity, ownership, environment, monitoring, policy, and authorization evidence.
- Display validation, duplicate, stale-update, and safe deletion-impact states.

### Endpoint details

- Endpoint identity, environment, confirmed health, maintenance overlay, owner, and enabled state.
- Authorized Run Now action returns a queued acknowledgement rather than blocking on HTTP.
- Sections: Overview, History, Incidents, SSL, Findings, Configuration, Audit.
- Show monitor source/configuration provenance and comparability warnings.

### Incidents

- Filterable list by severity, state, owner, client, environment, and age.
- Details show issue, status, assignment, evidence, timestamps, duration, recurrence, and append-only timeline.
- Acknowledge, assign, investigate, resolve, close, force-close, and reopen actions appear only when relevant, but are always protected server-side.
- Resolution and exceptional transitions require labels, reason/note, confirmation, validation, and concurrency handling.

### Reports

- Shared authorized filters, bounded `[start, end)` date range, selected filters, and as-of time.
- Summary, paginated table, accessible chart, and CSV action use the same query definition.
- Export describes UTC/ISO-8601 behavior and formula-safety handling.

### Error and empty states

- Dedicated 403, 404, 409/concurrency, 500, dependency-unavailable, validation, no-data, and no-results states.
- Production errors contain a correlation reference but no stack trace, query, secret, response body, or unsafe exception.
- Every state provides a safe next action; retry is offered only when appropriate.

## 6. Accessibility and interaction contract

- Target WCAG 2.1 AA as the practical baseline.
- Semantic landmarks, one logical page heading, and a skip-to-content link.
- Complete keyboard operation with predictable order and visible focus.
- Menus, drawers, tabs, dropdowns, dialogs, filters, and date controls follow appropriate ARIA patterns.
- Dialogs trap focus, support Escape where safe, and return focus to their trigger.
- Persistent labels, instructions, validation summaries, field associations, and programmatic error state.
- Status, severity, trend, and validation never rely on color alone.
- Charts have text/table alternatives.
- Actions announce queued/success/failure outcomes without disruptive focus changes.
- Support 200% zoom, reduced motion, and light/dark contrast where those modes are enabled.
- No hover-only content or critical action.
- Destructive actions state impact and require deliberate confirmation; safe undo is preferred where possible.

## 7. Security and performance

- Output-encode all untrusted labels, notes, target values, and diagnostics.
- Avoid generic raw-HTML rendering and unsafe inline event handlers.
- Define a Content Security Policy compatible with the selected components without broadly allowing unsafe inline scripts.
- Page initialization belongs in application-owned external scripts or approved nonce-based blocks.
- Load only plugins used by implemented pages and define bundle budgets after the licensed package is available.
- Do not publish proprietary source maps, license data, or source-package credentials.
- Verify dashboard P95 against representative data rather than assuming the template meets NFR-02.

## 8. Acceptance evidence

- [x] UI baseline and superseded direction recorded.
- [x] Primary responsive journeys and accessibility contract documented.

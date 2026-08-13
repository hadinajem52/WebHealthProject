# UI Direction — Purity UI Dashboard Figma

**Decision:** Use the provided Purity UI Dashboard Figma file as the application-shell and visual baseline for the server-rendered ASP.NET Core MVC application.
**Decision source:** Project direction received 2026-08-13.  
**Implementation status:** Figma baseline verified and implemented for the reusable shell in Phase 1; see [`../phase-1/Application_Shell.md`](../phase-1/Application_Shell.md). No vendor-template blocker remains.
**Approval:** Approved by the intern/project owner on 2026-08-13

## 1. Superseded direction

This decision supersedes the prior vendor-template direction and the custom-vanilla-CSS-only UI direction. It uses the provided Figma design as the visual reference while retaining semantic HTML, responsive behavior, accessibility, server-side authorization, anti-forgery, output encoding, performance, and safe error handling.

It does not supersede semantic HTML, responsive behavior, NFR-09 accessibility, server-side authorization, anti-forgery, output encoding, performance, or safe error handling.

## 2. Figma design reference and implementation gate

The authoritative visual reference is the provided [Purity UI Dashboard Figma file](https://www.figma.com/design/cjTsi6qaX3bH0l3a4vF7Jm/Purity-UI-Dashboard---Chakra-UI-Dashboard--Community-?node-id=0-1&p=f&m=dev). MCP verification confirmed the file contains dashboard, sign-in, sign-up, sidebar, analytics-card, table, and account-page layouts.

Before assets enter the repository:

- [x] Verify the provided Figma file and record its file key and node reference.
- [x] Record the dashboard, authentication, navigation, table, and account-page reference layouts.
- [x] Define the application-owned implementation approach for semantic HTML, styles, components, and accessibility behavior.
- [x] Keep Figma as the design reference; do not treat exported or generated assets as a substitute for application-owned code and accessibility requirements.

Do not scrape, hotlink, or copy assets from the public preview.

## 3. MVC integration

- Razor views and thin MVC controllers remain the presentation model.
- Keep MVC Tag Helpers, model binding, server validation, anti-forgery tokens, and server-side authorization.
- Keep application-owned styles and components reproducible and version-controlled.
- Use Figma-derived tokens and layout intent without copying inaccessible or unnecessary implementation artifacts.
- Add shared layouts/partials for the app shell, authentication, navigation, breadcrumbs, page toolbar, flash messages, validation, statuses, filters, tables, pagination, and empty states.
- Use JavaScript as progressive enhancement for menus, drawers, dialogs, tabs, and charts. Core navigation and forms must still have server behavior.
- Retain Chart.js for dashboard/report visualizations where appropriate.

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
- [x] Reusable shell, accessibility behavior, and error/empty states implemented in Phase 1. Evidence: [`../phase-1/Application_Shell.md`](../phase-1/Application_Shell.md). Journey-specific screens remain owned by Phases 2–5.

# Phase 5 Browser UI Test Scenarios

These scenarios translate `docs/phase-5/Phase_5_Ui_Test_Checklist.md` into measurable manual tests. They are executed against a running local application with seeded data where the required fixture exists. Each scenario records the observed result in `phase5_ui_test_results.csv`.

## Test environment and evidence rules

- Browser: Playwright CLI Chromium session.
- Evidence: URL, visible text/DOM state, computed style, request/console inspection, and download metadata where applicable.
- A scenario passes only when every stated expected condition is observed; otherwise it fails and receives a `BUG-P5-###` identifier.

## Scenario matrix

| ID | Area | Procedure and measurable acceptance criteria |
|---|---|---|
| P5-F-01 | Filters | Submit a client filter and verify the URL/query, `.filter-summary`, and retained control value name the selected client. |
| P5-F-02 | Filters | Submit each of the seven filter controls (`ClientId`, `WebsiteId`, `EnvironmentId`, `OwnerSubjectId`, `HealthStatus`, `MonitorType`, `WindowStart`/`WindowEnd`) and verify every submitted `select`/`input` retains its value after reload. |
| P5-F-03 | Disclosure | Load without filters and verify the summary explicitly says no filters are applied; verify inclusive-start/exclusive-end wording and an as-of instant are visible. |
| P5-F-04 | Validation | Submit a window over the configured maximum and a reversed window; verify a visible validation message, no blank page, and previously applied filters remain disclosed. |
| P5-F-05 | Clear/empty | Apply filters, activate Clear, and verify the unfiltered dashboard returns. Combine filters with no matching rows and verify a verbal empty state plus filter disclosure. |
| P5-X-01 | CSV delivery | On a filtered dashboard verify Download CSV query parameters exactly match applied filters; click it and verify Playwright download event, CSV content type/filename, UTF-8 BOM, and expected header. |
| P5-X-02 | CSV scope/identity | Navigate to page 2, download CSV, and verify the file contains the complete filtered set rather than only page 2; verify a visible row has identical CSV values. |
| P5-P-01 | Pagination | With multiple pages verify pager renders, Next advances, links preserve filters, an overlarge page resolves to the last populated page, and boundary controls are absent/inert. |
| P5-T-01 | Trend | Verify `canvas#dashboard-trend` renders with `aria-hidden`, the equivalent table is always present, row/series counts agree, and uptime/P50/P95 are all present. |
| P5-T-02 | Trend resilience | Disable JavaScript and verify page/table numbers still render. Block vendored Chart.js with a 404 and verify table remains intact, no unhandled page error appears, and all dashboard requests are same-origin. |
| P5-T-03 | Reduced motion | Emulate `prefers-reduced-motion: reduce` and verify chart is in its final rendered state without animation-dependent content. |
| P5-S-01 | Status/badges | Verify every status badge has visible text; inspect `.badge::before` computed non-`none` clip paths and verify shapes differ for success, warning, high, danger, and info. |
| P5-S-02 | High contrast | Emulate `forced-colors: active` and verify badges remain distinguishable by text/border/shape, not colour alone. |
| P5-C-01 | Certificates | Verify expired certificates display critical, other invalid certificates display invalid, counts sum to described monitors, and monitors without observations display unknown. |
| P5-D-01 | Diagnostics | Verify diagnostics card visibly renders overdue, in-flight, failed, and last-completed values (or an explicit empty state). |
| P5-I-01 | Incidents | Verify incident preview contains only active incidents and its row count equals the incident card count; filter to a client without incidents and verify list/count both become empty/zero; follow a row link and verify details resolves. |
| P5-A-01 | Authorization | For Administrator, Operations, Developer/Support, Viewer, and no-role users, exercise `/`, `/Reports/Export`, and `/Reports/Trend`; verify each is allowed or 403 per policy and never partially rendered. Verify a client-scoped Viewer sees only permitted data across dashboard/export/certificates/incidents. |
| P5-A-02 | Anonymous auth | In a signed-out context request each protected route and verify redirect to login and return to the originally requested page after sign-in. |
| P5-K-01 | Keyboard/focus | Tab through the page and verify skip link is first, filter controls follow visual order, every control is reachable and named, every control has visible focus-visible outline, keyboard submits filter, and focus moves to the main landmark. |
| P5-K-02 | Landmarks/axe | Verify exactly one banner, navigation, and main landmark; run axe on dashboard, incident list, and check history and fail on serious/critical violations. |
| P5-R-01 | Responsive | At 360px verify no body horizontal overflow, wide tables scroll in their container, stacked rows include `tbody th`, and stat grid/badge legend/filter summary wrap. |
| P5-O-01 | Output safety | Use HTML/quote/angle-bracket names and verify text-only rendering in dashboard/table/summary, no script execution, and matching `textContent`; verify CSV round-trip is not a formula. |
| P5-O-02 | Unicode | Verify non-ASCII names render correctly on screen and in the downloaded file. |
| P5-G-01 | Regression | On normal load verify no third-party request and no console error; with empty data verify every card displays an explicit empty state rather than an ambiguous measurement zero. |

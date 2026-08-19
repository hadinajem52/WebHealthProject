# Phase 5 — Browser-Driven UI Test Checklist (Playwright)

**Scope:** the Phase 5 user-facing surfaces — the dashboard, the trend chart, the reports/CSV
export, the certificate and diagnostics cards, and the incident preview.

**Why these and not more xUnit.** The existing suites already prove the *numbers* — uptime
eligibility, `[start, end)` windows, percentile inclusion, screen/CSV record identity, SSL
boundaries, CSV quoting and formula neutralisation. Repeating any of that through a browser would
test the same rule twice and the browser not at all. Everything below is a claim that can only be
falsified in a real browser: rendered output, navigation, form round-trips, focus and keyboard
behaviour, computed styles, downloads, and script failure modes.

**Prerequisite.** These need a signed-in session per role and a seeded dataset. The existing
integration factory stubs the readers, which is right for shell assertions and useless here — a
dashboard with no rows cannot demonstrate a pager. Plan for a real PostgreSQL fixture (the
`scripts/run-reporting-performance-baseline.ps1` seed is a reasonable starting shape, at a much
smaller scale) and a login helper that establishes a cookie per application role.

Legend: **[ ]** to write · *(covers …)* names the rule or the review finding it defends.

---

## 1. Filters, disclosure, and the shared query core

- [ ] Selecting a client and submitting reloads the page with that client applied, and the
      `.filter-summary` names it. *(BR-R01)*
- [ ] Every one of the seven controls round-trips: `ClientId`, `WebsiteId`, `EnvironmentId`,
      `OwnerSubjectId`, `HealthStatus`, `MonitorType`, `WindowStart`/`WindowEnd`. After submit, each
      `select`/`input` still shows the submitted value rather than resetting. *(BR-R02)*
- [ ] With no filter applied, the summary says so in words rather than rendering an empty list.
- [ ] The window is disclosed as inclusive-start / exclusive-end, and the as-of instant is present.
      *(BR-R01, BR-U04)*
- [ ] A window longer than the maximum renders a visible validation message and the previously
      applied filter is still shown — not a blank page and not a silent reset.
- [ ] A window whose end precedes its start is rejected the same way.
- [ ] The "Clear" action returns to the unfiltered dashboard.
- [ ] Combining filters that select nothing renders the empty state in words, with the filters still
      disclosed.

## 2. Screen and export agree — through the browser

- [ ] The "Download CSV" link's query string carries exactly the applied filter. *(AC-11)*
- [ ] Clicking it downloads a file: assert the `Content-Disposition` filename and that the
      response is CSV, via Playwright's download event.
- [ ] The downloaded bytes begin with a UTF-8 BOM and open with the expected header row.
      *(Excel compatibility — proven at the writer level, unproven over HTTP.)*
- [ ] Export from **page 2** of a filtered dashboard returns the whole filtered set, not that page.
      *(Directly defends the reviewer finding that the export contract must be explicit.)*
- [ ] A row visible on screen appears in the downloaded file with the same values.

## 3. Pagination

- [ ] With more monitors than one page, the pager renders and "Next" advances.
- [ ] Pager links preserve every applied filter, not just the page number.
- [ ] Requesting a page beyond the end lands on the last page with rows on it — never an empty table
      with no way back.
- [ ] "Previous" is absent or inert on page 1; "Next" likewise on the last page.

## 4. The trend chart and its table equivalent

- [ ] The `<canvas id="dashboard-trend">` renders and is `aria-hidden`.
- [ ] The equivalent table is **always** present — not behind a `<details>` — and its rows match the
      chart's series count.
- [ ] All three series appear in the table: uptime, P50, P95.
- [ ] **With JavaScript disabled**, the page still renders and the table still carries every number.
- [ ] **With the vendored Chart.js blocked** (route interception returning 404), the page renders,
      the table is intact, and no unhandled error surfaces — `dashboard.js` must fail silently.
- [ ] No request leaves for a third-party origin on dashboard load: assert every request URL is
      same-origin. *(Vendoring is asserted by string match today; this asserts it at runtime.)*
- [ ] Under `prefers-reduced-motion: reduce`, the chart is drawn in its final position.

## 5. Status, certificates, and non-colour encoding

- [ ] Each badge carries a visible text label, not colour alone.
- [ ] `.badge::before` has a non-`none` computed `clip-path`, and the shape differs between
      `success`, `warning`, `high`, `danger` and `info`. *(The CSS is asserted as source text today;
      this asserts what the browser computed.)*
- [ ] Under `forced-colors: active`, badges remain distinguishable.
- [ ] An expired certificate is shown as critical; one invalid for another reason is shown as
      **invalid**, never folded into the healthy count. *(Defends the 5.6 review finding.)*
- [ ] The certificate card's counts sum to the monitors it describes.
- [ ] A monitor with no certificate observation is reported as unknown rather than omitted.

## 6. Diagnostics and incidents

- [ ] The diagnostics card renders overdue, in-flight, failed and last-completed values.
- [ ] The incident preview lists only active incidents and its length agrees with the incident count
      on the card above it. *(Defends the "count and rows describe one selection" finding.)*
- [ ] Filtering to a client with no incidents empties the list *and* zeroes the count together.
- [ ] An incident row links through to its details page and the link resolves.

## 7. Authorization, driven through the browser

For each of Administrator, Operations, Developer/Support, Viewer, and a signed-in user with **no**
application role:

- [ ] `/` — dashboard renders or returns 403, per that role's policy.
- [ ] `/Reports/Export` with a filter — allowed or 403, and never a partially-rendered page.
- [ ] `/Reports/Trend` — allowed or 403.
- [ ] A Viewer scoped to one client sees only that client's rows on the dashboard, in the export, in
      the certificate card and in the incident list. *(This is the surface that was completely broken
      until 5.7 — the query failed to translate for every non-global role, so it deserves a browser
      test that would have caught a 500 on load.)*
- [ ] Anonymous access to each redirects to login and returns to the requested page afterwards.

## 8. Keyboard, focus, and labels

- [ ] Tab order through the filter form is visual order, and every control is reachable.
- [ ] Every control receives a visible `:focus-visible` outline.
- [ ] Each control has an accessible name — assert via role/name queries, not by reading `for`.
- [ ] The skip link is the first tab stop and moves focus to the main landmark.
- [ ] The filter can be submitted by keyboard alone.
- [ ] Landmarks are present and unique: banner, navigation, main.
- [ ] Run an automated accessibility scan (axe) on the dashboard, incident list and check history,
      failing on serious and critical violations.

## 9. Responsive behaviour

- [ ] At a narrow viewport the data tables stack, including `tbody th` — the row header must not stay
      in table layout while its row stacks around it.
- [ ] The page body never scrolls horizontally at 360 px; wide tables scroll within their own
      container.
- [ ] The stat grid, badge legend and filter summary wrap rather than overflow.

## 10. Output safety in the browser

- [ ] A client, website or endpoint named with HTML (`<script>alert(1)</script>`, quotes, angle
      brackets) renders as text on the dashboard, the table and the filter summary — assert the
      script never executes and `textContent` matches the stored name.
- [ ] The same name survives a CSV round-trip without becoming a formula.
- [ ] Non-ASCII names render correctly on screen and in the downloaded file.

## 11. Regression guards worth having

- [ ] The dashboard issues no request to an origin other than the application's.
- [ ] No console error is logged on a normal dashboard load.
- [ ] The dashboard renders with an empty database — every card shows its empty state rather than a
      zero that looks like a measurement.

---
s
## Deliberately not covered here

- Uptime, percentile and window arithmetic — covered by `ReportingQueryCoreAssertions` against a real
  cluster.
- CSV quoting, BOM and formula neutralisation at the field level — covered by `CsvWriterTests`. Only
  the delivery of that file over HTTP is a browser concern.
- SSL band boundaries — covered by `SslResultNormalizerTests`; only their *display* belongs here.
- Performance — covered by `ReportingPerformanceBaselineTests`. A browser timing assertion would
  measure the test runner as much as the application.

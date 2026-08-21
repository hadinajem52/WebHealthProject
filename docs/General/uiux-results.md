# UI/UX audit verdict

**Overall score: 7/10 — a strong engineering foundation, but not yet an efficient operational dashboard.**

The interface looks polished, consistent, and considerably more professional than a typical internal monitoring tool. The underlying implementation also shows unusually good attention to semantics, data integrity, responsive behavior, and accessibility.

The main weakness is **product hierarchy**: the page currently behaves more like a detailed reporting screen than a dashboard. It makes users work through filters, historical figures, and charts before reaching the information that normally matters most: **what is broken right now and what needs action**.

I reviewed the four desktop captures and the current dashboard markup, CSS, JavaScript, navigation, and reporting models. This is a static audit rather than a live keyboard, mobile-device, or usability test.

## Scorecard

| Area                    |  Score | Assessment                                              |
| ----------------------- | -----: | ------------------------------------------------------- |
| Visual polish           | 8.5/10 | Clean, professional and cohesive                        |
| Design consistency      | 8.5/10 | Strong reusable visual system                           |
| Information hierarchy   | 5.5/10 | Filters and history overpower urgent information        |
| Operational efficiency  | 5.5/10 | Too much scrolling and interpretation                   |
| Accessibility structure | 8.5/10 | Excellent semantic implementation                       |
| Accessibility visuals   |   5/10 | Several significant contrast failures                   |
| Data comprehension      | 6.5/10 | Accurate, but mixes different concepts and timeframes   |
| Responsive foundation   | 7.5/10 | Good code-level foundation; still needs browser testing |

## What is already strong

### The visual system is coherent

The spacing, rounded cards, typography, form controls, buttons, status badges and table styling all belong to the same design language. Nothing looks accidentally assembled from unrelated component libraries.

The selected sidebar state is obvious, card boundaries are subtle rather than noisy, and the teal accent is used consistently.

### The accessibility engineering is better than the visual result suggests

The implementation includes:

* A skip link and logical page heading.
* Persistent labels for every filter.
* Proper table headings, captions and row scopes.
* Text alternatives for the chart.
* Status labels and different status shapes rather than depending exclusively on color.
* Reduced-motion support.
* Responsive tables that become labeled blocks on narrow screens.

The chart canvas is removed from the accessibility tree while the same values are supplied in a table, and the dashboard sections use appropriate headings and labels.

The badge implementation also adds distinct silhouettes for success, warning, high, danger and informational states, which is an excellent non-color cue.

### The data-integrity model inspires trust

Every card, table, chart and export is intentionally derived from the same authorized reporting query. This is an important UX strength: users are less likely to see a summary count that disagrees with the visible rows because different screens silently applied different filters.

### The filter implementation is technically good

The eight controls are aligned well, labels remain visible, apply and clear actions are explicit, and the scope summary tells users what data they are looking at and when it was generated.

The responsive implementation moves from one to two to four filter columns, converts tables into labeled blocks on small screens, and changes the sidebar into a drawer below the desktop breakpoint.

---

# Main problems

## 1. The first viewport prioritizes configuration over health

This is the biggest UX problem.

On a normal desktop viewport, users see:

1. Header and search.
2. A very large filter panel.
3. Window and comparability metadata.
4. Only the beginning of the totals section.

They cannot immediately answer:

* Is anything currently broken?
* How many critical endpoints are there?
* What needs my attention?
* Is the monitoring pipeline itself working?

The active-incidents section is currently rendered **after totals, the daily trend, the current-health table, certificate expiry and diagnostics**.

For a monitoring product, the order should be urgency-first:

1. Current operational state.
2. Active incidents.
3. Endpoint health.
4. Historical performance.
5. Certificates and monitoring diagnostics.
6. Detailed filters and exports.

At minimum, move **Active incidents** directly below the top summary. Ideally, show a compact critical alert above everything else:

> **1 critical monitor · 2 active incidents**
> audicapital.com availability check is failing — acknowledged 18 minutes ago.

## 2. The page mixes “current state” and “selected-window history”

The calculations are valid, but the presentation creates apparent contradictions.

The screenshots show:

* Uptime: **100%**
* Clean: **50%**
* Current monitor status: **Critical**
* Active incidents: **2**

The model intentionally defines uptime as the percentage of eligible availability samples that answered during the selected period, while the confirmed monitor status is the latest state. It also defines “Clean” as the stricter percentage of responses with no warning findings.

That distinction is logically sound, but users should not have to reconstruct it from small explanatory sentences.

Split the summary into two visibly separate groups:

### Current status

* 1 critical monitor
* 3 healthy monitors
* 2 active incidents
* Scheduler healthy
* Certificates healthy

### Selected window

* Availability: 100%
* Warning-free responses: 50%
* Median response: 682 ms
* P95 response: 1.7 s

Also rename:

* **Uptime** → **Window availability**
* **Clean** → **Warning-free responses** or **Healthy-response rate**
* **Totals** → **Performance over selected period**

“Clean” is too ambiguous for a headline metric.

## 3. Active incidents do not receive enough visual importance

The critical state is represented by one small red badge among otherwise equal-weight cards. Everything has almost identical card treatment regardless of urgency.

The active-incidents table also omits the most useful triage detail: **what actually failed**. It shows endpoint, severity, workflow status, opened time and owner, but no incident title, monitor type or failure reason.

A better incident row would show:

> **Availability check failed**
> audicapital.com · HTTP 503 · Production
> Critical · Acknowledged · Open for 18 minutes · Administrator

The incident count in the summary should also be clickable.

## 4. The search field appears functional but currently is not

The header search has normal input styling and accepts text, but the layout explicitly describes it as a placeholder for later work and it is not connected to a form or search behavior.

That creates a false affordance: users can type into a prominent control and receive no result, message or next action.

Until search is implemented:

* Remove the field completely, or
* Render it visibly disabled with “Search coming soon.”

The first option is better. It would free a large amount of header space.

## 5. Several color combinations fail accessibility contrast

This is the most important implementation-level problem.

The repository itself records that parts of the sidebar deliberately retain the Figma colors even though they fall below WCAG AA.

Examples calculated from the current tokens:

| Element                                       | Approximate contrast | Requirement |
| --------------------------------------------- | -------------------: | ----------: |
| Inactive sidebar text `#A0AEC0` on `#F8F9FA`  |               2.14:1 |       4.5:1 |
| White support-card text on teal `#4FD1C5`     |               1.87:1 |       4.5:1 |
| White “Healthy” badge text on green `#48BB78` |               2.43:1 |       4.5:1 |
| 10px table headers `#A0AEC0` on white         |               2.26:1 |       4.5:1 |
| Secondary table text `#718096` on white       |               4.02:1 |       4.5:1 |

The table headers are especially difficult to read because they combine **very small 10px text** with a low-contrast gray. The CSS currently sets those exact values.

Recommended changes:

```css
:root {
    --sidebar-label: var(--color-gray-600);
    --sidebar-support-surface: var(--color-teal-600);
    --sidebar-support-text: var(--color-white);
}

.data-table th {
    color: var(--color-gray-600);
    font-size: 0.75rem;
    font-weight: 700;
}

.data-table__secondary {
    color: var(--color-gray-600);
}

.badge[data-status="success"] {
    background-color: var(--status-success-text);
}
```

An alternative for success badges is a pale green background with dark-green text, which would also reduce visual aggression.

## 6. The filter area is excellent as a report filter, but oversized for a dashboard

Eight large fields, a technical hint, a divider, two actions and a scope summary consume most of the first screen.

A better dashboard pattern would be:

> **All clients · All websites · Production · Last 30 days**
> `Filters` `Refresh` `Export`

Pressing **Filters** could open an inline expanded panel or accessible side drawer containing the complete eight-field form.

Keep the full filtering functionality, but make it secondary to the dashboard’s main purpose.

Additional filter issues:

* The date controls are blank even though a default rolling window is active.
* The fields are labeled UTC, while the summary in the screenshot displays GMT+3.
* “Inclusive” and “exclusive” are technically precise but too implementation-oriented for the main interface.
* **Clear** is enabled even when the summary says no filters are applied.

A more approachable summary would be:

> **Last 30 days · All clients · Updated 20 seconds ago**

The exact timestamps and `[start, end)` behavior can remain available in a tooltip or details disclosure. The current summary partial deliberately renders the precise window and as-of values.

The comparability warning should also be a recognizable warning banner rather than another gray sentence inside the metadata strip.

## 7. The dashboard is unnecessarily long

For only four monitors and two incidents, the complete page requires substantial scrolling.

Contributors include:

* A full eight-field filter panel.
* Five large statistic tiles.
* A large dual-axis chart.
* A complete daily data table below the chart.
* A nine-column current-health table.
* Two large certificate/diagnostic cards.
* The incident table at the bottom.

The dashboard should provide a concise operational snapshot and link to detailed report pages for deeper exploration.

The code’s always-visible chart data table is an admirable accessibility choice, but it creates considerable sighted-user duplication. One compromise is to keep the table semantically available through an accessible **View chart data** disclosure rather than showing every daily row by default.

## 8. The chart asks users to interpret too many concepts simultaneously

The trend chart combines:

* Uptime percentage on the left axis.
* P50 response time on the right axis.
* P95 response time on the right axis.

Different shapes and dash patterns are a strong accessibility choice, but dual-axis charts are still cognitively expensive and may imply a relationship between uptime and response latency that is not actually meaningful.

Split it into:

1. **Availability trend**
2. **Response-time trend**

They can share the same date range and sit next to each other on wide screens.

The JavaScript also sets `spanGaps: true` for all three series. That joins points across missing data. In monitoring software, missing data is operationally significant and should normally appear as a visible gap or “No data” region rather than a continuous line.

Add threshold lines as well:

* Availability target, such as 99.9%.
* P95 warning threshold.
* P95 critical threshold.

Without thresholds, users see “1703 ms” but are not told whether it is acceptable.

## 9. The current-health table will not scale cleanly

The table is well constructed, but its information model is monitor-centric rather than endpoint-centric.

Each endpoint appears twice:

* Once for `HttpAvailability`
* Once for `SslCertificate`

This creates repetitive rows and leaves irrelevant cells containing dashes and zero samples.

The raw monitor values are also technical:

* `HttpAvailability`
* `SslCertificate`

Use friendly labels:

* Availability
* SSL certificate

A more scalable endpoint row could be:

| Endpoint        | Environment | Availability | SSL     |    P95 | Last checked | Incidents |
| --------------- | ----------- | ------------ | ------- | -----: | ------------ | --------: |
| audicapital.com | Production  | Critical     | Healthy | 942 ms | 2 min ago    |         2 |

Additional improvements:

* Make the incident count a link or badge.
* Show relative timestamps first, with exact timestamps on hover or focus.
* Group multiple monitor details beneath an expandable endpoint row.
* Add sticky headers for larger result sets.
* Display hosts in normal casing rather than visually aggressive uppercase URLs.

## 10. Certificate and diagnostics cards waste vertical space

The two cards share one grid row, so the short certificate card stretches to match the taller diagnostics card. This produces the large empty white region visible in the screenshot.

The card grid currently uses normal grid stretching. Adding this would allow each card to retain its own natural height:

```css
.card-grid {
    align-items: start;
}
```

The certificate summary is also noisy when everything is healthy. Showing red “0 critical” and “0 invalid” badges still introduces alarm colors.

Prefer:

> **2 healthy certificates**
> No certificates require attention.

Only render warning, high, critical and invalid badges when their counts are nonzero.

## 11. The sidebar information architecture needs consolidation

The sidebar currently gives separate top-level entries to:

* SEO
* Broken links
* PageSpeed
* Reports

Several use the same chart icon, making them difficult to distinguish visually. The navigation definition confirms the repeated report icon and the planned entries.

A clearer structure would be:

* Dashboard
* Registry
* Incidents
* Maintenance
* **Audits**

  * SEO
  * Broken links
  * PageSpeed
* Reports
* Administration

The large **Need help?** card is also misleading: it does not provide help or documentation; it links to runtime health endpoints. Runtime health already appears in Diagnostics and the footer, so the same technical destinations are represented three times.

Either change it to actual documentation/support, or replace it with a compact **System status** item.

## 12. The page title hierarchy is too weak

The page currently displays:

* Breadcrumb: Dashboard
* H1: Dashboard
* Card heading: Monitoring overview

The breadcrumb and title repeat each other, while the actual H1 is visually only 14px. As a result, “Monitoring overview” looks more like the page title than “Dashboard.”

For a top-level page:

* Remove the breadcrumb.
* Increase the H1 to approximately 24–28px.
* Add one concise subtitle such as “Current availability, incidents and performance.”

---

# Recommended dashboard composition

A stronger desktop layout would be:

### Header

**Dashboard**
All clients · Production · Last 30 days · Updated 20 seconds ago
`Refresh` `Filters` `Export`

### Operational status

| Current health        | Active incidents | Monitoring system | Certificates |
| --------------------- | ---------------- | ----------------- | ------------ |
| 1 critical, 3 healthy | 2 acknowledged   | Running normally  | 2 healthy    |

### Active incidents

A compact two- or three-row incident table with failure reason, duration and direct actions.

### Endpoint health

One row per endpoint, with availability and certificate states combined.

### Selected-window performance

| Availability | Warning-free responses | Median |   P95 |
| -----------: | ---------------------: | -----: | ----: |
|         100% |                    50% | 682 ms | 1.7 s |

### Trends

Separate availability and latency charts.

### Secondary operational information

Certificates and scheduler diagnostics, shown compactly and expanded only when something needs attention.

# Final verdict

**The dashboard should keep its existing visual system and engineering foundation. It does not need a wholesale visual redesign. It needs a hierarchy and density redesign.**

The current implementation is:

* Visually polished.
* Technically thoughtful.
* Semantically strong.
* Trustworthy in how it computes and scopes data.

But it is also:

* Too filter-first.
* Too long.
* Insufficiently incident-focused.
* Ambiguous about current versus historical state.
* Carrying several real accessibility contrast failures.
* Presenting a nonfunctional search control as if it worked.

After correcting contrast, removing the fake search, separating current status from selected-window performance, moving incidents to the top and compacting the filters, this could realistically become an **8.5/10 production-quality monitoring dashboard**.

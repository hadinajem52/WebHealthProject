# Dashboard, Trends, and Reports UI

**Work item:** Phase 5 / increment 5.6
**Rules:** BR-R01, BR-R02, BR-U06, BR-C04, BR-P05
**Acceptance criteria contribution:** AC-06 display, AC-11 through the shared query core delivered in 5.5.
**Also closes:** the two Phase 2 accessibility boxes — keyboard/focus/label/responsive coverage, and status that does not rely on colour.

## The dashboard is a view over the shared query core, not a second implementation

`HomeController.Index` normalizes its query string through the same `ReportQueryNormalizer` the CSV export uses, then reads everything from `IReportingReader` with that one `ReportQuery`. The cards, the health table, the trend series, the incident list and the "Download CSV" link are therefore five renderings of one dataset. Changing a filter recomputes all of them consistently because there is nothing else to recompute (BR-R01).

The disclosure strip is built from the query the *reader served* rather than the one the form submitted, so a page number the reader clamped is shown as the page actually served rather than as the number that was asked for.

The export link is built from `DashboardFilterViewModel.ToRouteValues()` — the same object that produced the query — so the file a user downloads is the filter they were looking at, not a second interpretation of the same URL.

The dashboard carries the same `[Authorize(Policy = ReadRegistry)]` every other read surface does. Leaning on the authenticated-user fallback would let a signed-in account with no application role reach it and see an empty page rather than a denial — hiding the authorization decision inside query behaviour instead of stating it at the boundary.

Three reads sit beside the dataset rather than inside it:

- **Certificate expiry** is deliberately outside the reporting window. Expiry is a property of the certificate presented *now*; if it were computed from samples inside the window, narrowing the window would change how soon a certificate appears to expire. The band is re-derived from the day count stored with the observation, so the dashboard, the endpoint page and the check that raised the finding always agree (BR-C04). Validity and expiry are reported as two facts, not one: an expired certificate is critical, and one that is invalid for another reason — untrusted, hostname mismatch, not yet valid — has no meaningful band and is counted as **invalid**. Folding those into the healthy count would let a broken certificate raise the reassuring number.
- **Diagnostics** answers a different question from every other card: whether checks are *running*. A dashboard showing green because nothing has been checked since yesterday is worse than one that says so, hence overdue monitors, in-flight work, failed work items and the last completed check.
- **Active incidents** come from `QueryActiveIncidentsAsync` on the same reader, over the same selected monitors and the same active statuses the summary counts. Reading them through the incident reader's own filter — which knows nothing about the dashboard's client, website, environment or owner — is how a dashboard ends up showing a count for one dataset and rows for another.

`ReportTooLargeException` is caught and shown as a filter error. A filter selecting more monitors than one report may aggregate over is a request to narrow it, not a page that should quietly take a long time.

## What every screen now discloses (BR-R01)

`_FilterSummary` renders the applied filters, the window and the read instant. It is built from the **normalized** query rather than the submitted form, so what it names is what was actually applied — including a window the server defaulted or bounded.

The window is stated as what it is: `… (inclusive) to … (exclusive)`. A reader comparing two adjacent periods needs to know that a check at midnight belongs to the second one (BR-U04), and the only reliable way to convey that is to write it down.

An unfiltered view says "None — everything you have access to" rather than showing a blank row, because "everything you can see" and "nothing was selected" are different statements.

The strip is on the dashboard, the incident list and the check history. It is a definition list rather than prose so assistive technology can move term by term instead of hearing one long sentence.

## Chart.js is vendored, and the chart is never the only route to the data

Chart.js 4.4.4 is carried under `wwwroot/lib/chartjs`, with its licence, exactly as bootstrap and jquery are. No CDN is referenced, so rendering the dashboard reaches no third-party origin and the page cannot break because someone else's host is down. A test asserts both halves: no CDN hostname in the markup, and the vendored file is actually served.

The canvas is hidden from assistive technology outright (`aria-hidden`), and the same numbers are always rendered in a plain table beside it — not behind a disclosure. A generic `aria-label` on a canvas hands a screen reader a description of data instead of the data, and a table folded inside `<details>` is data a reader has to go looking for. A reader without script, with the library blocked, or using a screen reader loses nothing — which is also why `dashboard.js` fails silently: a broken canvas must not take the page with it.

All three series the table lists are plotted: uptime, P50 and P95. The chart honours `prefers-reduced-motion` by drawing in its final position rather than animating into it, and each series differs by dash pattern and point shape as well as by colour.

## Accessibility: the two Phase 2 boxes

### Status does not rely on colour

Every `.badge` gets a shape from `.badge::before`, drawn with `clip-path`:

| Status | Silhouette | Reading |
|---|---|---|
| success | circle | settled, nothing to act on |
| warning | triangle | the conventional caution shape |
| high | diamond | a notch above warning, distinct from both neighbours |
| danger | octagon | the stop shape |
| info | bar | deliberately unlike the four verdicts |

The rule targets `.badge` itself rather than a partial, so **every** badge in the application carries the cue — including the two dozen written before this increment and any written after. That is why the shape approach was chosen over adding an icon to a new partial: a partial only helps the views that remember to use it.

The shape is `clip-path` on an empty pseudo-element, not generated text, so no screen reader announces it. The visible label remains the primary cue; the shape is redundancy for sighted readers who cannot rely on the fill. Under `forced-colors: active` the fills collapse to the system palette, so the shape becomes the only remaining distinction and is repainted in `CanvasText` to keep its contrast.

`_StatusBadge` still exists, but only for what a shape and a label cannot do: attaching extra wording for a screen reader when a visible label is too terse to stand alone — "30 days or fewer remaining" behind a pill that only says "3 warning".

### Keyboard, focus, labels, responsive

- **Focus** was already global (`:focus-visible` in `shell.css`) and the dashboard adds no control that escapes it.
- **Labels**: every control on the dashboard filter is labelled, and the filter group carries an accessible name so a screen reader announces what it is for before reading its controls. Six labels on the incident lifecycle forms were adjacent to their controls but not associated with them — `for`/`id` pairs now bind them, with distinct identifiers so the force-close and reopen forms cannot collide.
- **Tables**: the responsive stacking rules covered `td` but not `th` in the body, so the row header — the cell carrying the record's identity — stayed in table layout while everything around it stacked. The rule now includes `tbody th`, which is what makes the new tables usable on a phone, since they all use `<th scope="row">` for the endpoint.
- **Responsive**: the stat grid, the badge legend and the filter summary all wrap rather than scroll. A reader must never have to pan sideways to find out what a figure was filtered to.
- **Landmarks and skip link** were delivered in Phase 1 and are unchanged; the shell tests still assert them.

## Verification evidence

`ApplicationShellTests`, against the running application with the reporting readers stubbed — these tests are about the shell, not the data:

- `Dashboard_RendersTheSharedShellLandmarks` — the skip link, landmarks and breadcrumbs survive the rewrite.
- `Dashboard_DisclosesTheAppliedFiltersAndTheAsOfInstant` — BR-R01, including that the window states its exclusive end.
- `Dashboard_SaysSoWhenNoFilterIsApplied` — the unfiltered case is stated rather than blank.
- `Dashboard_RejectsAnOutOfBoundsWindowInsteadOfServingIt` — the server-side bound surfaces as a visible error.
- `StatusBadges_CarryANonColourCue` — the shape rules and the forced-colours fallback exist in the served stylesheet.
- `TrendChart_IsVendoredLocallyAndHasATableEquivalent` — no CDN reference, and the vendored library is served by this application.
- `EmptyState_DescribesMissingDataWithText` — the empty states describe what is missing in words.

The dashboard's *numbers* are not tested here. They come from the reporting query core, whose evidence runs against a real PostgreSQL cluster in increment 5.5 — testing them again through the view would test the view twice and the query not at all.

## Every filter dimension is reachable (BR-R02)

The form exposes client, website, environment, owner, health status, monitor type and the window — every dimension `ReportQuery` carries. A filter the query layer applies but the primary screen cannot express would leave part of the shared contract unreachable, and would leave the export link unable to represent what the user is looking at. All of them travel through `ToRouteValues()`, so the pager and the "Download CSV" link carry the same filter the page is showing.

The current-health table also renders the owner it already loaded, so a filter by owner can be checked against the rows it produced.

# Phase 5 — Browser Verification Evidence

**Date:** 2026-08-19
**Method:** the real application driven in Chromium via Playwright, over HTTPS, against the seeded
performance fixture (96 endpoints, 192 monitors, 1,667,520 samples, 90 days of history).
**Scheduling was disabled** for the run, so the application could not issue checks against the
fixture's fictional hosts.

This is a manual verification pass, not an automated suite. The suite to commit is
[the UI test checklist](Phase_5_Ui_Test_Checklist.md); this document records what was observed when
the checklist was walked by hand, and what it found.

## Result

Everything checked behaved correctly. No defects were found in this pass.

That is worth stating carefully rather than triumphantly: this pass exercised the surfaces the
checklist calls out, on one dataset, in one browser. It did not exercise every role, and it is not a
substitute for the automated suite.

## Verified

### Authorization and visibility scope

| Check | Result |
|---|---|
| Anonymous request to `/` | Redirected to login with `ReturnUrl` preserved |
| Viewer scoped to one client — dashboard | Renders; **32 monitors**, client 00 only |
| Viewer — CSV export | 32 rows, client 00 only; visibility applies to the export |
| Viewer — certificate card | Scoped to their 16 certificate monitors |
| Viewer — navigation | Users, Teams, Audit and Maintenance absent |
| Viewer — direct requests to admin surfaces | Denied (redirect to access denied) |
| Viewer — filter by a client they have no grant for | **0 rows on screen, 0 in CSV**; the client is named "No longer visible" rather than disclosed |

The scoped-viewer case matters most: until the defect found during increment 5.7, the reporting
query failed to translate for every non-global role, so this exact page returned a server error. It
now renders, and the scope demonstrably restricts both the screen and the export.

A filter naming a client the reader cannot see returns an empty result **without confirming the
client exists** — the summary says "No longer visible" rather than echoing its name.

### Reporting core (AC-11)

| Check | Result |
|---|---|
| CSV response | `200`, `text/csv; charset=utf-8`, `attachment` with a dated filename |
| Encoding | UTF-8 BOM present, CRLF line endings, ISO-8601 with offset |
| Screen row vs CSV row | Identical values — endpoint, status, 2,722 eligible samples, P95 381 ms, 1 incident |
| Export requested from page 1 | 192 rows |
| Export requested from **page 3** | **192 rows** — the whole filtered set, not that page |
| Pagination | 192 monitors over 8 pages of 25 |
| Page 999,999 | Clamped to "Page 8 of 8" with the remaining 17 rows and no Next |

### Filters and disclosure (BR-R01, BR-R02)

All seven controls round-trip and are labelled. The filter summary names applied filters by display
name, states the window as `… (inclusive) to … (exclusive)`, and carries the as-of instant. An
unfiltered view says "None — everything you have access to". The export link carries exactly the
applied filter as its query string.

Rejected filters are shown rather than silently reset — an over-long window renders
*"Filter not applied — The report window cannot be longer than 366 days."*

### Certificates (BR-C04)

Counts partition exactly: 88 healthy + 3 warning + 1 high + 1 critical + 3 invalid + 0 unknown = 96,
the SSL monitor count. **Untrusted certificates are counted as invalid, never folded into healthy**,
and sort ahead of expiring ones in the attention list despite having 115–314 days remaining. Bands
honour 30/15/7: warning at 16 days, high at 11, critical at 2.

### Trend chart and its table

Chart.js **4.4.4**, served from this application; three datasets (uptime, P50, P95) of 29 points
each, distinguished by dash pattern *and* point style (circle, triangle, rectRot), with ~82,000
painted pixels confirming it actually drew. The canvas is hidden from assistive technology through
its `aria-hidden` wrapper, and the equivalent 29-row table is present **in the server-rendered
HTML**, so a reader without script loses nothing.

### Accessibility and responsive

Every status carries a distinct shape in the browser's own computed style, not merely in the
stylesheet source:

| Status | Computed shape |
|---|---|
| success | 10×10 circle (`border-radius: 50%`) |
| warning | triangle (`clip-path`) |
| high | diamond |
| danger | octagon |
| info | 3×12 bar |

At a 360 px viewport there is no horizontal overflow, `tbody th[scope="row"]` computes
`display: block` so the row header stacks with its row, and wide tables scroll inside their own
container. Focus is a visible 2.4 px solid outline.

### Output safety

A client created through the UI named `<script>alert('xss')</script> & "quoted"` with notes
`"><img src=x onerror=alert(2)> — naïve ünicode ✓`:

- no dialog fired, and no `script` or `img` element was injected;
- the served HTML contains `&lt;script&gt;alert(&#x27;xss&#x27;)`, never a live tag;
- the name renders as text in the heading and in the dashboard's client filter;
- non-ASCII characters survive intact.

### Runtime hygiene

Nine requests on a dashboard load, **all same-origin** — Chart.js, fonts and images served locally,
no third-party host contacted. Zero console errors or warnings.

## Not covered by this pass

- **Operations and Developer/Support roles.** Only Administrator and a scoped Viewer were driven.
- **The no-JavaScript and blocked-library paths** were inferred from the server-rendered HTML rather
  than driven with script disabled or the library blocked by route interception.
- **CSV formula neutralisation end to end.** The guard is unit-tested; reproducing it in the browser
  needs a monitor whose owning client carries a formula-triggering name, which this fixture has none
  of.
- **An automated accessibility scan.** Landmarks, labels, focus and non-colour cues were checked
  individually; no axe run was performed.
- **Anything about the ninety-day dashboard window**, which remains over its NFR-02 budget.

## Reproducing

1. Start the baseline cluster and seed it — `scripts/run-reporting-performance-baseline.ps1`
   leaves a suitable database behind when run with `-KeepCluster`.
2. Create an administrator against it:
   `dotnet run --project src\WebHealth.Web -- --bootstrap-admin` with `BootstrapAdmin__*` and
   `ConnectionStrings__WebHealth` set.
3. Run the application with `Monitoring__Scheduling__Enabled=false` and `--no-launch-profile`, so
   `ASPNETCORE_URLS` is honoured and no checks are dispatched at the fixture's fictional hosts.
   HTTPS is required: the authentication cookie is `Secure`, so it will not persist over HTTP.

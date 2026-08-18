# Shared Reporting Query Core and CSV Export

**Work item:** Phase 5 / increment 5.5
**Rules:** BR-U01, BR-U02, BR-U03, BR-U04, BR-U05, BR-U06, BR-R02, BR-R03, BR-P05
**Acceptance criteria contribution:** AC-11 — dashboard filters and CSV export return the same logical dataset.

## AC-11 is held by construction, not by agreement

The whole shape of this increment exists to make one sentence true: **there is one filter object and one query layer, and every reporting surface goes through both.**

- `ReportQuery` is the only filter. It has an `internal` constructor, so the only way to obtain one is through `ReportQueryNormalizer` — a caller cannot hand-build one with an unbounded window or a negative page.
- `ReportingReader.SelectMonitors` is the only place a filter or a visibility scope is applied, and it stays an unexecuted `IQueryable`, so counting, paging and identifier collection all run in PostgreSQL against one composition. Every public method — the screen, the export, the certificate card, the diagnostics card and the incident list — composes from it with the same `ReportQuery`.
- **The incident list is one of them.** Reading it through a separate filter is exactly how a dashboard ends up showing a count drawn from one dataset and rows drawn from another, so `QueryActiveIncidentsAsync` runs over the same selected monitors and the same active statuses the summary counts.
- One instant is read per call and used for visibility scoping and for every "is this overdue" comparison, so the freshness a screen displays is the instant its data was selected at rather than an approximation of it.
- `ReportCsv` formats rows. It contains no `Where`, no sort and no authorization check. If it ever grows one, the screen and the export have stopped being the same dataset, and that is the line to watch in review.

### What the export actually contains

The export is **the whole filtered set**, not the page the screen happens to be on. `ExportAsync`
calls `ReportQuery.ForExport()` on whatever it is handed rather than trusting the caller to have
sliced it, so a request arriving from page 3 of the dashboard still produces the complete file. A
file silently containing only rows 51–75 would be read as the complete answer.

Its page size is `MaximumMonitors` — the same limit the selection refuses to exceed. The two are
one constant, so an export can never be a truncated file: a filter too wide to export is refused
outright when it is selected, rather than served with a header quietly admitting it is partial.

AC-11's test is therefore a direct record-identity comparison: the export's rows and the
concatenation of every screen page are the same records from the same query, so anything less than
equality is a defect.

## Server-side bounds

Every bound lives in `ReportQueryNormalizer`, so a crafted request cannot widen anything:

| Bound | Value | Why |
|---|---|---|
| Default window | 30 days ending now | a report with no window is a report of everything |
| Maximum window | 366 days | one year plus a leap day, the longest span anyone asks for by name |
| Screen page size | 25 | matches every other list in the application |
| Page | clamped to ≥ 1 | a nonsensical page is a navigation slip, not an attack |
| Status, monitor type | rejected if unrecognised | a typo must not silently return everything |
| Monitors per report | 5,000, else refused | bounds the selection, the identifier array the aggregates receive, **and** the export |

The bounds are invariants of `ReportQuery`, not conventions its callers follow. `WithPaging` and
`ForExport` both re-apply them, so there is no way to derive a query with a zero page size — which
would later divide by zero in the pagination arithmetic — or a page below one.

Both window bounds are resolved to UTC *before* anything is compared, so a request carrying a local offset selects the same instants as one carrying `Z`.

A filter selecting more than 5,000 monitors raises `ReportTooLargeException`, which is a request to narrow the filter rather than a page that quietly takes a long time.

## The window is half-open (BR-U04)

`[WindowStart, WindowEnd)` in UTC. A sample measured exactly at the end instant belongs to the next period, never this one, so adjacent periods can never double-count a check. Every surface that renders a window states its exclusive end in words, because that is the only reliable way to convey it to a reader comparing two periods.

## Rows are per endpoint monitor

Uptime, monitor type and confirmed health are all properties of a *monitor*, not of an endpoint — an HTTPS endpoint has two of them. Reporting per monitor makes the monitor-type filter natural and keeps a certificate monitor's row honest: it appears, with no eligible samples and a null uptime, rather than being hidden or shown as zero per cent.

The ordering is a total order — client, website, environment, URL, monitor type, then the monitor identifier as a final tiebreaker — so paging is stable and page *N* of the screen and page *N* of an export contain the same records in the same sequence.

Confirmed health comes from the latest `endpoint_health` row (BR-U06), and a monitor that has never confirmed anything reads as `Unknown`: a state to show, not a row to hide.

## Uptime (BR-U01–BR-U03)

The denominator is `check_result.counts_for_uptime`, which the finalizer already set at write time to exclude manual runs, cancelled runs, maintenance-suppressed runs and certificate checks. Reapplying those rules in the query would be a second definition of eligibility that could drift from the one the data was written under.

**Uptime is healthy over eligible, as BR-U01 says.** Every sample category is written in SQL as
what it *is* — `= 'Healthy'`, `= 'Warning'`, `= 'Critical'` — never as "not failed". A negatively
phrased predicate silently absorbs any outcome added later, which is exactly how a new quality rule
could end up counted as uptime without anyone having decided that it should be.

That leaves a real question: a warning sample answered, so it is not downtime, but it is not
healthy either. Rather than resolving it by fiat in one direction, both readings are reported and
neither has to be inferred:

- `Percentage` — healthy / eligible. BR-U01 as written.
- `ReachablePercentage` — (healthy + warning) / eligible. What an operator often means by "up".

The summary reports all five counts alongside them: eligible, healthy, warning, down, and excluded.
BR-U02 asks for the included and excluded counts to be shown, and this is where that comes from —
the exclusion is visible rather than something an operator has to infer from a number that looks
lower than expected.

Uptime is null, not zero, when a window held no eligible sample. "No data" and "nothing was up" are different statements.

## Percentiles: `percentile_cont`, and only over successful samples (BR-U05)

The choice is recorded because it is the kind of thing that gets silently changed later.

**`percentile_cont`, not `percentile_disc`.** Response time is a continuous quantity. `percentile_cont` interpolates between the two samples that straddle the rank, so P95 moves smoothly as samples arrive; `percentile_disc` must return an actually-observed value, which makes the P95 of a small window jump between discrete readings. The cost is that a reported percentile may be a millisecond value no single check produced — the normal trade for a continuous statistic, and the reason the sample count is reported next to it.

**Over samples that responded.** The ordering covers eligible samples whose outcome is `Healthy`
or `Warning` — a warning sample is a completed exchange whose duration is a real measurement, which
is why the percentile denominator is not the same set as the uptime numerator. A failed exchange's
duration is its timeout budget; admitting it would drag P95 toward the timeout setting instead of
describing how the site performs. The database gate proves this with a 15,000 ms timeout sitting in
the window: P95 comes out at 1,520 ms, not near 15,000.

The trend series uses the same predicates as the summary, so a chart and the card above it can
never disagree about the same window. The gate asserts that too, on the day whose only sample was
the timeout.

Aggregation runs in PostgreSQL rather than in memory. The percentiles need `percentile_cont`, and pulling every sample back to compute uptime client-side would make a year-long window a multi-million-row transfer.

## Trend series

Daily buckets via `(measured_at AT TIME ZONE 'UTC')::date`, over eligible samples, ordered by day. The buckets use UTC because the window's own boundaries do, so a chart bucket and a report period never disagree about which day a sample belongs to. The 366-day window bound is also the bucket-count bound.

## CSV (BR-R03)

`CsvWriter` is pure and produces bytes, not a stream of concerns:

- **UTF-8 with a byte-order mark.** Without it Excel opens a UTF-8 file as the local ANSI code page and mangles every non-ASCII name in it — which is exactly the Arabic-and-other-Unicode case BR-R03 names.
- **RFC 4180 quoting.** A field containing a comma, a quote, CR or LF is quoted; embedded quotes are doubled. Rows end with CRLF.
- **ISO-8601 with the offset spelled out** (`yyyy-MM-ddTHH:mm:ss.fffzzz`), so a reader never has to guess the zone.
- **Stable column names**, defined once in `ReportCsv.Headers`.
- **A row with the wrong field count is rejected** rather than padded or truncated, because either would shift every later column against its header.

### The formula-injection guard applies to user text only

A spreadsheet evaluates a cell beginning with `=`, `+`, `-`, `@`, tab or CR as a formula rather than showing it. Tab and CR are included because Excel strips leading whitespace before deciding, so `\t=cmd` reaches the formula parser just as `=cmd` does. Guarded fields are prefixed with an apostrophe, which spreadsheets read as "the rest of this cell is text". The apostrophe becomes part of the exported value: this changes what the file says in order to stop the file being executable, which is the trade the guard exists to make. The guard runs before the quoting rule, so the apostrophe ends up inside the field's own quotes.

`CsvField` carries whether a cell came from somewhere a person could control, and the guard applies only to `CsvField.Text`:

| Factory | Guarded | Used for |
|---|---|---|
| `Text` | yes | client, website, environment, endpoint URL, owner — things a user typed |
| `Token` | no | identifiers, statuses, monitor types — a closed vocabulary this system defines |
| `Number`, `Count`, `Flag`, `Timestamp`, `Date` | no | values this system formatted itself |

A blanket guard would rewrite the perfectly ordinary value `-1` as `'-1` and corrupt every negative number in the file, to defend against a risk that generated numerals do not carry. That is why the distinction exists rather than a single "escape everything" pass.

## Comparability in reports (BR-P05)

The summary carries the same `ComparabilityAssessment` the check-history page uses, evaluated over exactly the samples the report aggregated. One definition of "comparable" across the application, rather than a reporting-specific rule that could disagree with the check page about the same data.

## Verification evidence

### Pure unit tests

- `CsvWriterTests` — the byte-order mark, CRLF terminators, RFC 4180 quoting including embedded quotes and newlines, a guarded field that also needs quoting, every formula trigger, machine-formatted values left unguarded, nulls as empty fields, and a wrong-width row rejected.
- `ReportQueryNormalizerTests` — the default window, UTC resolution across offsets, a window that does not end after it starts, the maximum window at and past the limit, page clamping, each selectable status, `Disabled` rejected as a registry state rather than a monitoring outcome, unknown monitor types rejected, blank values treated as absent, all validation failures reported together, and `WithPaging` changing only the slice.

### Database gate

`ReportingQueryCoreAssertions` runs against a real PostgreSQL cluster, because the parts that can go wrong here are the parts PostgreSQL evaluates:

- **Uptime counts only eligible samples** — a seeded window of 6 eligible (4 healthy, 1 warning, 1 critical) and 2 ineligible produces 83.3333 %, with the excluded count reported; a certificate monitor's row appears with no eligible samples and a null percentage.
- **The window is half-open** — samples sit on both boundary instants; the one at the start is counted, the one at the end appears only in the next period.
- **Percentiles use successful samples only** — durations of 100/200/300/400/1,800 ms give P50 = 300 and P95 = 1,520 (interpolated, as `percentile_cont` does and `percentile_disc` would not), while a 15,000 ms timeout in the same window stays out of the ordering entirely.
- **The trend buckets by UTC day** — ascending, unique, inside the window, and summing to the summary's eligible count.
- **Screen and CSV select the same records** — for every combination of client, website, environment, owner, status and monitor type filters (including a client that matches nothing), the export is handed a query *still sitting on the screen's page* and must still return the whole set; its rows are compared to the concatenation of the screen's pages with record equality, and the CSV bytes are parsed back the way a recipient would read them and checked against the same rows. A counter asserts the combination set was not empty, so the loop cannot pass vacuously.
- **The incident list and the incident count describe one selection** — a filter matching nothing yields both an empty list and a zero count, and over the fixture the list length equals the summary's count with every row in an active status.
- **A page beyond the end reports the page it served** — a request for page 999,999 comes back as the last page with rows on it, so the pager never renders an unreachable page number.
- **Visibility applies to every surface** — a viewer with no grant sees nothing through the screen, the export, the incident list or the certificate card. Testing each separately matters: a surface that skipped the visibility scope would be a disclosure route no assertion on another surface would catch.

**This gate has not yet been executed.** It compiles and is wired into `DatabaseFoundationAssertions.VerifyAsync`, but the PostgreSQL cluster run (`scripts/run-database-foundation-tests.ps1`) has not been performed for this increment, so the assertions above are written evidence rather than observed evidence. Everything in the pure-unit list above is passing.

## Known limitations

- The reporting endpoints delivered here are the CSV export and the trend series. The dashboard that reads them arrives in [increment 5.6](Dashboard_Trends_And_Reports_Ui.md), which also exposes every filter dimension as a control.
- Query-plan evidence and the decision on whether daily aggregates are needed belong to increment 5.7.

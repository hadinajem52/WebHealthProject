# Live Run Progress (Broken Links + PNG Image Audit) — Implementation Plan

**Repository:** `hadinajem52/WebHealthProject`
**Reviewed baseline:** `cb149a317e98e9db6b19718044c891856685920b`
**Suggested path:** `docs/General/Live_Run_Progress_Implementation_Plan.md`
**Status:** **Ready for implementation**
**Feature:** live progress for `Broken links` and `Tools → PNG image audit`
**Revision:** amended after review — the live client is a separate script, PNG gates on
`ImagesDiscovered`, and hazards 7–12 were added

---

# 1. Goal

While a crawl or PNG audit is running, the page must show progress as it happens instead of sitting
still until the run finishes.

Today both pages poll, but they poll for *completion*:

* Broken links shows `0 pages crawled` for the entire run, because `crawl_run.pages_fetched` is
  written once, at completion.
* PNG shows nothing during the discovery phase, because the whole site crawl finishes before the
  first result is persisted.

The transport is not the problem. The missing piece is **progress data**, plus a hot polling path
cheap enough to run every second.

Target latency is **1–3 seconds** end to end (up to ~2s write throttle plus up to ~1s poll). This is
*live* progress, not literal real time, and the UI copy must not claim otherwise.

---

# 2. Locked decisions

| Concern | Decision |
| --- | --- |
| Transport | Polling. No SignalR, SSE, WebSockets, or event bus |
| Progress writes | Throttled, max one per ~2s, on the awaited execution path |
| Hot status poll | ~1s, backing off to ~2s while unchanged |
| Status response | Small JSON when changed, `204` when unchanged |
| Authorization | Existing `VisibleRuns(access)` scope, never a bare id lookup |
| Invisible or nonexistent run | Same `404` for both |
| `ajax.js` | Left untouched |
| `run-status.js` | Left untouched. The live client is a new `live-run-status.js` |
| Poller | Add `resetBackoff()`, shared by both clients |
| Counter updates | Direct `textContent` patching, neutral tone while active |
| Crawl comparison | Never on the live path |
| Result-row deltas | None, in either section |
| Result tables | Dedicated Razor partial actions, count-gated, max once per ~3s |
| Table refresh trigger | Crawl `BrokenLinkCount`, PNG `ImagesDiscovered` — never `ImagesAnalyzed` |
| Deferred refresh | Dirty flag, re-evaluated on every poll including `204` |
| Completion | One final full fragment refresh |
| `phase` persistence | Not added |
| Schema migration | None |

---

# 3. Verified baseline facts

Established by reading the code at the baseline commit. Recorded so the plan does not get
re-derived, but re-check any that a future change could invalidate.

**Already incremental**

* Crawl link rows are written after every page visit: `CrawlExecutionService.cs:315` calls
  `DrainAsync`, which calls `RecordLinksAsync`.
* `CrawlRunSummary.BrokenLinkCount` is a live `COUNT` over those rows
  (`CrawlReportReader.cs:189`), so broken-link counts already tick up.
* PNG run counters are written after every image (`PngAuditResultSink.cs:707-724`), and the PNG run
  detail page already re-queries image rows on each poll.

**Not incremental**

* `crawl_run.pages_fetched` and `links_recorded` are written only by `RecordRunOutcomeAsync`
  (`CrawlResultSink.cs:198-207`).
* PNG writes nothing until discovery finishes; `DiscoverAsync` returns before the first
  `RecordAsync` (`PngAuditExecutionService.cs:82-149`).

**In-memory counters available to checkpoint**

* Crawl: `_pagesFetched` (`CrawlExecutionService.cs:347`) and `_linksRecorded`
  (`CrawlExecutionService.cs:600`).
* PNG crawler accumulates pages, HTTP attempts, and page bytes internally and only surfaces them in
  `PngSiteDiscoveryResult`.

**Cost of the current hot path**

* The Broken links poll URL *is* `Index` (`Crawl/Index.cshtml:8`), so every tick runs
  `ListRunsAsync`, `CompareLatestAsync`, and `DescribeTestBlockAsync` (`CrawlController.cs:35-41`).
* `CompareLatestAsync` runs correlated semi/anti-joins over two runs' broken links
  (`CrawlReportReader.cs:61-115`). It filters to `Status == Completed`, so **the running run cannot
  participate in it at all** — recalculating it during a run is provably wasted work.
* `RecordBatchAsync` costs roughly eight aggregate queries per image before
  `UpdatePartialSummariesAsync` (`PngAuditResultSink.cs:640-724`). It must not be reused for
  discovery progress.
* `RecordLinksAsync` already ends in `CountAsync` per drain (`CrawlResultSink.cs:110`). Checkpoints
  reuse the in-memory value; they must not add another count.

**Authorization**

* Both readers self-filter list queries through `VisibleRuns(access)` (`CrawlReportReader.cs:158-162`,
  `PngAuditReader.cs:60-62`), so a partial action that skips its own existence check leaks no rows —
  but it renders an empty table instead of a `404`.

**Client behaviour**

* `replaceFragment` throws before touching the DOM when the response lacks the requested region
  (`ajax.js:276-281`).
* `focusResponse` only moves focus when `status >= 400 || source` (`ajax.js:220`). The poller passes
  `source = null` with a `200`, so repeated refreshes do not steal focus or jump scroll.
* `data-preserve-animation` keeps spinners from restarting across swaps (`ajax.js:229-268`).
* `createPoller` resets `attempt` only inside `start()`, which also resets `startedAt`
  (`poller.js:135-146`).
* **`run-status.js` is shared by seven views**, not just these two: `Checks/Check.cshtml:386`,
  `Checks/History.cshtml:155`, `PageAudits/Index.cshtml:693`, plus both Crawl and both PNG views.
  Its `DEFAULT_INTERVALS` and its `ajax.load` refresh are module-global.
* `AjaxFragmentViewModel` already carries `RefreshUrl` alongside `StatusUrl`, and `ajax.js:436`
  awaits `refreshFragment` **before** `dispatchResponse` at `ajax.js:438` — the event
  `run-status.js` listens on to begin polling.
* PNG `AnalyzedCount` **excludes** `FetchFailed`, `HttpNonSuccess`, and `ResponseTruncated`
  (`PngAuditResultSink.cs:649-653`), while `ImageCount` is every persisted row. They diverge
  whenever an image fails to fetch.
* PNG discovery seeds `ConsumedHttpAttempts` and `ConsumedPageBytes` from stored values but starts
  its page collection empty each attempt (`PngAuditExecutionService.cs:82-95`).
* `DrainAsync` already serializes persistence behind the `_dataAccess` semaphore across crawl
  workers (`CrawlExecutionService.cs:596-608`).
* The broken-links card header renders tone from the count itself —
  `data-status="@(run.BrokenLinkCount == 0 ? "success" : "warning")"` plus a matching icon
  (`Crawl/Run.cshtml:219-221`).
* `CrawlRunViewModel.HasMore` is `BrokenLinks.Count == PageSize` and `ListBrokenLinksAsync` takes
  exactly `limit`, so a region model may reuse that rule verbatim.

---

# 4. Hazards this design exists to avoid

1. **A `204` must never reach `ajax.load`.** `readPayload` treats an empty body as HTML,
   `response.ok` is true for `204`, `replaceFragment` throws, the catch at `ajax.js:552` renders a
   persistent error banner, and `isRetryable(204)` is false so the poller stops permanently. The live
   fetch path handles `204` itself and never routes through `ajax.load`.
2. **A progress checkpoint must not fail a finished run.** `UpdatePartialSummariesAsync` throws when
   it updates zero rows. The new checkpoint methods must return `bool` and treat zero as a no-op,
   because a checkpoint can legitimately race completion.
3. **Result rows cannot be appended.** Both tables sort semantically, not by arrival:
   `CrawlReportReader.cs:170-174` and `PngAuditReader.cs:65-68`. Appending corrupts order and the
   `Showing n–m` counter.
4. **A partial must render its own wrapper element,** because `replaceFragment` replaces the matched
   element rather than its contents. A partial that emits only rows triggers hazard 1's failure mode.
5. **PNG live writes must stay inside snapshotted limits.** `ValidatePersistedLimits` throws when
   `PagesDiscovered > MaxPages` (`PngAuditResultSink.cs:686-698`), which would fail the run.
6. **Checkpoints stay on the awaited path.** No fire-and-forget. The claim/lease guards make a late
   write a safe no-op, but only ordering keeps the final totals authoritative.
7. **The live client must not regress the four other pages on `run-status.js`.** Converting its
   `refresh()` to JSON, or widening its module-global interval list, would break check history,
   individual checks, and PageSpeed audits, which still expect HTML fragment polling. The live
   protocol goes in a new script.
8. **PNG's manual run starts from a pre-run DOM.** `RunNow` returns only `StatusUrl`, which works
   today because the status URL returns HTML that renders the active row, the spinner, and the live
   host. Against a JSON endpoint the browser would sit on markup containing no `[data-live]`
   elements to patch. `RunNow` must also return `RefreshUrl`.
9. **PNG table refreshes must not gate on `ImagesAnalyzed`.** A run whose images all fail to fetch
   adds visible result rows while `ImagesAnalyzed` never moves, so the table would not refresh until
   completion. Gate on `ImagesDiscovered`, the persisted row count.
10. **A skipped refresh must stay pending.** If the count changes during the table cooldown and the
    client has already accepted the new version, later `204`s mean it may never reconsider. A dirty
    flag re-evaluated on every poll outcome is required.
11. **Live PNG progress must not move backwards.** A second attempt after a lost lease rediscovers
    from zero while the stored count is already high, so raw callback values would visibly regress
    the counter.
12. **Patched counters must not carry stale severity.** Writing `textContent` alone leaves a
    zero-broken success badge green while displaying `4`.

---

# 5. Phases

Each phase is independently demonstrable. Crawl goes first because it is the larger visible win and
the smaller change; PNG follows once the shape is proven.

## Phase 1 — Crawl progress persistence

**Goal:** `pages_fetched` and `links_recorded` become true while the run is in flight.

**Files**

* `src/WebHealth.Application/Crawling/CrawlContracts.cs`
* `src/WebHealth.Infrastructure/Crawling/CrawlResultSink.cs`
* `src/WebHealth.Infrastructure/Crawling/CrawlExecutionService.cs`

**Work**

Add to `ICrawlResultSink`:

```csharp
Task<bool> UpdateProgressAsync(
    Guid runId,
    Guid executionClaimId,
    int pagesFetched,
    int linksRecorded,
    CancellationToken cancellationToken = default);
```

Implement as a single `ExecuteUpdateAsync` guarded exactly like `RecordRunOutcomeAsync`:

```csharp
.Where(run => run.Id == runId
    && run.Status == CrawlRunStatuses.Running
    && run.ExecutionClaimId == executionClaimId)
```

Return `updated == 1`. Never throw on zero.

In `CrawlExecutionService`, checkpoint from the existing drain path using `_timeProvider`, at most
once per two seconds, writing the in-memory `_pagesFetched` and `_linksRecorded`.

**No forced final checkpoint is needed.** `RecordRunOutcomeAsync` already writes both counters as
part of the authoritative completion update.

**Exit evidence:** start a crawl, watch `pages_fetched` climb in the database, confirm the final row
still matches the outcome write.

---

## Phase 2 — Crawl live status endpoint

**Goal:** a status response cheap enough to serve every second.

**Files**

* `src/WebHealth.Application/Crawling/CrawlContracts.cs`
* `src/WebHealth.Infrastructure/Crawling/CrawlReportReader.cs`
* `src/WebHealth.Web/Controllers/CrawlController.cs`

**Work**

Add `GetLiveStatusAsync(Guid runId, RegistryAccessContext access, CancellationToken)` to
`ICrawlReportReader`, projecting through `VisibleRuns(access)` and returning `null` when the run is
not visible.

Add `GET /Crawl/Runs/{id}/Status?version=`:

1. Project the run row. `null` → `this.NotFoundRecord("crawl run")`.
2. Build `version` as `{Status}:{PagesFetched}:{LinksRecorded}`.
3. Version matches the query parameter → `204 No Content`, no further queries.
4. Version differs → run the broken-link count, return JSON.

```json
{
  "active": true,
  "version": "Running:42:781",
  "pages": 42,
  "links": 781,
  "broken": 12
}
```

Set `Cache-Control: no-store`. The controller's existing `ReadRegistry` policy still applies.

**The Broken links index needs no new HTML action.** It stops polling `Index`, polls this endpoint
with the active run id it already renders, and performs one `Index` refresh at completion. That
removes `CompareLatestAsync` from the hot path without a second view path to maintain.

**Exit evidence:** unchanged polls return `204` with a single query; a changed poll returns JSON with
two; a run belonging to another owner returns `404`.

---

## Phase 3 — Crawl result table partial

**Goal:** refresh the broken-links table without rebuilding the whole run model.

**Files**

* `src/WebHealth.Web/Views/Crawl/_BrokenLinksTable.cshtml` (new)
* `src/WebHealth.Web/Views/Crawl/Run.cshtml`
* `src/WebHealth.Web/Controllers/CrawlController.cs`

**Work**

**Split at the results body, not the whole card.** The card header depends on `run.BrokenLinkCount`
and `run.CoveredWholeScope`, which the partial action must not have to load. Extract only the empty
state, table, and pagination:

```html
<div id="crawl-broken-links-results" data-ajax-region>
    <!-- empty state OR table and pagination -->
</div>
```

The header stays in `Run.cshtml` and its count is patched by the JSON client.

Introduce a region model rather than reusing `CrawlRunViewModel`:

```csharp
public sealed record CrawlBrokenLinksRegionViewModel(
    Guid RunId,
    IReadOnlyList<CrawlBrokenLink> BrokenLinks,
    int Offset,
    int PageSize,
    bool CoveredWholeScope)
{
    public bool HasMore => BrokenLinks.Count == PageSize;
    public int PreviousOffset => Math.Max(0, Offset - PageSize);
    public int NextOffset => Offset + PageSize;
}
```

`CoveredWholeScope` is required: the offset-zero empty state wording depends on it
(`Crawl/Run.cshtml:231-240`). `HasMore` matches the existing rule exactly.

Add `GET /Crawl/Runs/{id}/Results?offset=` which:

1. calls `GetLiveStatusAsync(id, access, ...)` as the visibility and existence probe — `VisibleRuns`
   is private to `CrawlReportReader` and is not callable from a controller;
2. returns `this.NotFoundRecord("crawl run")` when that is `null`;
3. calls `ListBrokenLinksAsync`;
4. returns `PartialView` of the region.

Leave the pagination links targeting `#ajax-page` as they are. They are user-initiated navigation,
where a full-page swap is the correct behaviour.

Add `data-live` attributes to the counter elements the client will patch, on both the run page and
the active row of the index history table.

**Note:** no controller in the repository returns `PartialView` today. Apply the same shape in both
controllers in Phase 5 so it reads as one convention.

**Exit evidence:** requesting the results URL directly returns only the table region; the run page
still renders identically on a normal load.

---

## Phase 4 — Client live path

**Goal:** counters update every second without HTML round trips.

**Files**

* `src/WebHealth.Web/wwwroot/js/poller.js`
* `src/WebHealth.Web/wwwroot/js/live-run-status.js` (new)
* Crawl and PNG views, to load the new script instead of `run-status.js`

**Work**

Add to the poller's returned object:

```javascript
resetBackoff: function () { attempt = 0; }
```

Do **not** call `start()` to speed up after a change — it resets `startedAt` and would extend the
configured lifetime indefinitely.

**`run-status.js` is not modified.** Four other views depend on its HTML-fragment protocol and its
module-global interval list. The live protocol goes in `live-run-status.js`, which reuses
`poller.js` and is loaded only by the Crawl and PNG views. Its own interval profile is roughly
`[1000, 1000, 1500, 2000, 2000]`; the shared one stays as it is.

The live host declares its URLs explicitly:

```html
data-live-run-status-url="..."
data-live-run-final-url="..."
data-live-run-results-url="..."
data-live-run-results-selector="#crawl-broken-links-results"
data-live-run-version="Running:0:0"
```

Client rules:

* `204` → nothing changed, no DOM work, keep polling.
* `200` → patch `[data-live]` elements by `textContent`, store the new version, call
  `resetBackoff()`.
* result count changed → set `resultsDirty = true`. Do not refresh inline.
* on **every** poll outcome including `204` → if `resultsDirty` and the ~3s cooldown has elapsed and
  focus is not inside the results region, `ajax.load` the region and clear the flag. A refresh
  skipped for cooldown or focus stays pending rather than being lost.
* `active: false` → stop the hot poll, discard any pending table-only refresh, run **one** full
  fragment refresh (`Run` on the detail page, `Index` on the index), and stop. Without this final
  refresh the page counts up and never renders its finished state.
* `404`/`5xx` → reuse the existing retry and expiry behaviour; never leave the page in a live state
  with no polling and no explanation.

While a run is active, render changing issue counters with a neutral or `info` tone so a patched
number cannot contradict a stale severity colour. The final full refresh restores the real
success/warning tone. This keeps badge styling on the server.

Existing visibility and offline pausing comes free from `poller.js` and is unchanged.

**Exit evidence:** counters advance on a running crawl; the table refreshes only when the broken
count moves; the page transitions cleanly to its completed state.

---

## Phase 5 — PNG parity

**Goal:** the same behaviour for PNG, including the discovery phase.

**Files**

* `src/WebHealth.Application/PngAudits/PngDiscoveryContracts.cs`
* `src/WebHealth.Application/PngAudits/PngPersistenceContracts.cs`
* `src/WebHealth.Infrastructure/PngAudits/PngSiteCrawler.cs`
* `src/WebHealth.Infrastructure/PngAudits/PngAuditExecutionService.cs`
* `src/WebHealth.Infrastructure/PngAudits/PngAuditResultSink.cs`
* `src/WebHealth.Infrastructure/PngAudits/PngAuditReader.cs`
* `src/WebHealth.Web/Controllers/PngAuditsController.cs`
* `src/WebHealth.Web/Views/PngAudits/` (`Run.cshtml`, `Index.cshtml`, new results partial)

**Work**

Define a discovery-layer record in `PngDiscoveryContracts.cs` so the crawler does not depend on a
persistence type:

```csharp
public sealed record PngSiteDiscoveryProgress(
    int PagesDiscovered,
    int HttpAttempts,
    long TotalPageBytes);
```

Extend `IPngSiteCrawler.DiscoverAsync` with an optional callback:

```csharp
Func<PngSiteDiscoveryProgress, CancellationToken, ValueTask>? progress
```

The crawler reports in-memory progress after meaningful page work and stays unaware of persistence.
`PngAuditExecutionService` maps it to `PngAuditCrawlProgress` and throttles to ~2s.

**Normalize before writing.** Discovery restarts its page collection from empty on a retried
attempt, so a raw callback value would drive the visible counter backwards from a previous
attempt's total:

```csharp
PngAuditCrawlProgress Normalize(PngSiteDiscoveryProgress reported) => new(
    Math.Max(stored.PagesDiscovered, reported.PagesDiscovered),
    Math.Max(stored.HttpAttempts, reported.HttpAttempts),
    Math.Max(stored.TotalPageBytes, reported.TotalPageBytes));
```

Add a single-statement sink method:

```csharp
Task<bool> UpdateCrawlProgressAsync(
    Guid runId,
    Guid leaseToken,
    PngAuditCrawlProgress progress,
    CancellationToken cancellationToken = default);
```

writing only `pages_discovered`, `http_attempts`, and `total_page_bytes`, guarded by
`Status == Running && LeaseToken == leaseToken`, returning `bool`, never throwing on zero rows. It
must not call `RecordBatchAsync` and must not run aggregates.

Make the write monotonic in SQL as a second line of defence, clamped to the snapshotted limits:

```sql
pages_discovered = GREATEST(pages_discovered, @pages),
http_attempts    = GREATEST(http_attempts, @attempts),
total_page_bytes = GREATEST(total_page_bytes, @bytes)
```

Clamp live values below the snapshotted limits so `ValidatePersistedLimits` cannot fail the run. The
existing `Math.Max(stored.PagesDiscovered, discovery.Pages.Count)` reconciliation
(`PngAuditExecutionService.cs:99`) stays as-is: `stored` is read before discovery, so the
post-discovery write is always at least the live value within an attempt.

As with crawl, no forced final checkpoint: `CompleteAsync` writes the authoritative totals, and
`ValidateCompletionTotals` still requires them to equal the persisted rows.

Then mirror Phases 2–4: version
`{Status}:{PagesDiscovered}:{ImagesDiscovered}:{ImagesAnalyzed}:{RecommendationCount}`, a JSON status
endpoint, and a results partial with its own region wrapper — its model needs only the run id, the
selected filter, and the paged image results. The existing HTML `Status` action stays as the
full-refresh path.

**Gate the table on `ImagesDiscovered`, not `ImagesAnalyzed`.** Expose it as `resultCount` in the
JSON so the trigger's meaning is obvious at the call site.

**`RunNow` must also return `RefreshUrl`,** or the browser stays on the pre-run DOM with no active
row and no `[data-live]` elements to patch:

```csharp
return Accepted(new AjaxFragmentViewModel(
    message,
    "information",
    RefreshUrl: Url.Action(nameof(Index), new { endpointId }),
    StatusUrl: Url.Action(nameof(LiveStatus), new { id = result.RunId }),
    RunId: result.RunId));
```

`ajax.js` awaits the refresh before dispatching the event that starts polling, so the live DOM
exists first. Crawl needs no equivalent change: its run form redirects rather than starting through
this AJAX flow.

**Exit evidence:** page count advances during discovery; image counts advance during analysis; final
totals match the persisted rows.

---

## Phase 6 — Verification

* Run `scripts\run-database-foundation-tests.ps1` (~4 minutes) and confirm a green run. Only a green
  run is evidence; the first failure hides every later stage.
* Drive both pages against a real endpoint and watch a full run start to finish.
* Confirm no new migration is pending and the schema assertion lists are untouched.

---

# 6. Tests to add

Only what the delivery principles require.

1. **Authorization, per role, direct request** for each new endpoint — `Crawl/Runs/{id}/Status`,
   `Crawl/Runs/{id}/Results`, and the PNG equivalents. A run outside the caller's visible endpoints
   must return `404`, not `403` and not an empty table. Existing homes:
   `tests/WebHealth.IntegrationTests/CrawlRunAuthorizationTests.cs` and
   `PhaseSixViewAuthorizationTests.cs`.
2. **Checkpoint racing completion** — a progress write against a completed or re-leased run returns
   `false` and leaves the final counters untouched. This is the one race that could turn a successful
   run into a failed one.
3. **PNG live progress never regresses** — a second attempt reporting fewer rediscovered pages than
   the stored total leaves `pages_discovered` unchanged. Covers both the `Normalize` call and the
   `GREATEST` write.

No test for the throttle interval itself, no test for the cooldown timing, and no test that
re-asserts what the existing crawl and PNG execution suites already cover.

---

# 7. Out of scope

* SignalR, SSE, WebSockets, Redis, or any push transport.
* A persisted `phase` column, or any schema change.
* Delta or cursor-based streaming of result rows.
* Generic hardening of `ajax.js` against empty-body replacement. The live fetch path never reaches
  it. Worth doing eventually, tracked separately, not bundled here.
* New indexes. Measure first; the existing `ix_crawl_link_result_run_classification` and the PNG
  classification and recommendation indexes cover the queries this plan adds.

---

# 8. Setup impact

None. Every column this plan writes already exists — `pages_fetched`, `links_recorded`,
`pages_discovered`, `http_attempts`, `total_page_bytes` — so there is no migration, and no change to
`ExpectedMigrations`, `ExpectedTables`, `TablesAddedAfterPhaseThree`, the per-table column
assertions, or any hand-written INSERT in a negative test.

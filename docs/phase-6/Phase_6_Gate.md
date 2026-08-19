# Phase 6 gate — views, authorization, and acceptance evidence

**Work item:** Phase 6.8
**Covers:** AC-07, AC-08, and the AC-09 regression

## 1. What 6.8 added

Two read surfaces, in the established dashboard style, plus the authorization work that makes them
safe to expose:

| Surface | Route | Reader | Policy |
|---|---|---|---|
| SEO | `/Seo` | `ISeoReader` | `ReadRegistry` |
| Broken links | `/Crawl`, `/Crawl/Run` | `ICrawlReportReader` | `ReadRegistry` |
| Maintenance | `/Maintenance` | `IMaintenanceReader` | `OperateMonitoring` |

Maintenance already had its view from Phase 4, and 6.1's occurrences surface through it
(`Next occurrence`, `Materialised occurrences`), so this increment did not rebuild it.

Both new surfaces are **read-only for every persona that may read the registry**. An SEO value or a
broken link is an observation about a site, not an operational control. The policy that decides what
a site *should* declare — the robots exception and the configured sitemap — stays behind
`ManageRegistry` where 6.4 put it.

## 2. The decision that comes first — a filter is a query, not a trim

Every filter on these pages is a predicate the **database** applies, inside the requester's
visibility scope. None of them is applied to a list the reader already fetched.

That is not a performance preference. A view that fetched broadly and filtered afterwards has
already read rows the requester is not entitled to, and the disclosure is complete at that moment
whether or not the rows are rendered. So `SeoReader` and `CrawlReportReader` both begin from
`RegistryVisibility.ApplyEndpointScope` and narrow from there.

### 2.1 An id in a URL is a parameter, not a permission

`ICrawlReportReader` originally took an endpoint id with no access context, which would have let any
authenticated user read another client's crawl results by guessing one. Every method now takes the
requester's `RegistryAccessContext` and resolves ids **through** the scope:

- an endpoint outside the scope lists no runs;
- a run outside the scope is `null` from `FindRunAsync` and empty from `ListBrokenLinksAsync`;
- `/Crawl/Run` answers **404, not 403**, because answering "forbidden" would confirm that the run
  exists.

### 2.2 The comparison is bounded without being truncated

`CompareLatestAsync` performs the set difference **in the database** and returns, per bucket, an
exact count plus a capped sample. Both halves matter:

- loading both runs' broken links to subtract them in memory is unbounded, and a crawl of a thousand
  pages can hold many thousands of distinct source-target rows;
- bounding the *set* rather than the *display* would be worse than slow. A previous run whose links
  were cut short reports the missing ones as **resolved** — the page-limit failure this phase spent
  6.7 designing out, reappearing through the reader.

So the counts are `COUNT(*)` over the scoped query, the samples are `Take(25)`, and the page says
"Showing 25 of 312" with a link to the run's own paginated detail.

### 2.3 One definition of "visible endpoint"

`CrawlReportReader` excludes deleted endpoints, matching `SeoReader` and the registry. An endpoint
removed from the registry should not keep answering through a different surface, and two read
surfaces holding different implicit definitions of visibility is how they drift apart.

### 2.4 Unrecognised filter values are not errors

A stale bookmark should show the unfiltered list rather than a failure, so an applicability or
environment value the controller does not recognise becomes *no filter*. It is never passed through
to the database as an unknown string.

## 3. Reporting a partial crawl honestly

The broken-link view never renders a budget-limited run as a clean result. Status and stop reason are
separate columns, and `CrawlRunDisplay` renders "Completed (partial)" with the reason it stopped —
"Stopped at the page limit — the site was not fully covered".

The comparison section refuses to compare at all unless two **full-scope** runs exist, and says so
in words rather than rendering four empty buckets that would read as "nothing changed". The
`Indeterminate` bucket is shown alongside New/Continuing/Resolved for the same reason: a link that
timed out this time has not been shown to be fixed.

## 4. Acceptance evidence

### AC-07 — SEO checks report title, description, canonical, indexing and robots problems

| Evidence | Where |
|---|---|
| Extraction from a real parser, values only, document never retained | `SeoValueExtractorTests` |
| Applicability, title, description rules | `SeoExtractionRuleTests`, `SeoRuleEvaluatorTests` |
| Canonical and production `noindex` policy | `SeoRuleEvaluatorTests` |
| `robots.txt` parsing, precedence, wildcard disallow | `RobotsTxtParserTests`, `RobotsRuleEvaluatorTests` |
| Per-origin snapshot, sitemap availability, recorded exception | database foundation gate |
| The view, its server-side filters and its roles | `PhaseSixViewAuthorizationTests`, `/Seo` |

### AC-08 — a crawl stays in scope, respects limits, and reports a broken internal link with its source page

| Evidence | Where |
|---|---|
| URL identity, revisit prevention, tracking parameters, explosion caps | `CrawlUrlNormalizerTests`, `CrawlFrontierTests` |
| Seeds, allowed hosts, path prefixes | `CrawlScopeTests` |
| Termination on a fully connected site | `CrawlFrontierTests.Frontier_TerminatesOnASiteWhereEveryPageLinksToEveryOther` |
| Page, depth, duration, concurrency and per-host rate limits | `CrawlExecutionTests`, `HostRequestRateLimiterTests` |
| **A broken internal link reported with its source page** | `CrawlExecutionTests.ExecuteAsync_ReportsABrokenInternalLinkWithItsSourcePage` |
| One result per source-target pair | `CrawlLinkLedgerTests`, database foundation gate |
| Cancellation preserves findings, never labelled complete | `CrawlExecutionTests`, `CrawlLinkLedgerTests` |
| Robots respected; override only for an approved non-production origin | `CrawlRobotsGateTests`, `CrawlExecutionTests` |
| Crawler does not starve monitoring | `CrawlIsolationTests` |
| Schema, uniqueness, cascade, query plan | database foundation gate |
| New/continuing/resolved/indeterminate comparison | database foundation gate |
| The view, its scoping and its roles | `PhaseSixViewAuthorizationTests`, database foundation gate |

### AC-09 regression — maintenance suppression still behaves

AC-09 was accepted in Phase 4. Phase 6.1 expanded recurrence into materialised occurrences, which
touches the same suppression path, so the regression is re-run rather than assumed:

| Evidence | Where |
|---|---|
| Recurrence expansion, DST boundaries, idempotent re-expansion | `MaintenanceRecurrenceTests`, `MaintenanceIntervalTests` |
| Suppression during an active occurrence, and result retention | database foundation gate |
| Occurrences surfaced in the maintenance view | `/Maintenance` |

## 5. What this phase deliberately did not do

- **Crawl scheduling from the UI.** `CrawlRunJob` exists with its own queue and worker budget, but
  nothing enqueues it yet, and it still takes `CrawlLimits.Default` with scope derived from its
  seeds. Closing that properly needs a stored crawl profile loaded server-side by id; passing limits
  as loose job arguments would put the bounds that keep a crawl safe into the queue payload, where
  nothing validates them.
- **Retention enforcement.** Defined in `Crawl_Schema_And_Comparison.md` section 5; Phase 7 owns it.
- **Daily aggregates and long-term rollups.** Deferred with retention, for the same reason Phase 5
  declined to size rollups against a policy that did not exist yet.
- **A bounded endpoint-option query.** The crawl picker uses `ListAllEndpointsAsync`, which returns
  every visible endpoint with no page bound. That is the established registry pattern —
  `TargetsController` uses the same call the same way — so narrowing it is a registry-wide change
  rather than a crawl one. Recorded here so it is a known shape rather than an oversight.

## 6. Running the evidence

```
dotnet test tests/WebHealth.UnitTests/WebHealth.UnitTests.csproj
dotnet test tests/WebHealth.IntegrationTests/WebHealth.IntegrationTests.csproj
scripts\run-database-foundation-tests.ps1
```

The database foundation gate is the slow one — roughly four minutes, one ordered test with the crawl
stage after the SSL stage. Only a green run is evidence that the later stages pass, because the
first failure hides every stage after it.

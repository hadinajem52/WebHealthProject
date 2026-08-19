# Bounded crawl execution and isolation (BR-L02, BR-L05 to BR-L10)

**Work item:** Phase 6.6
**Acceptance contribution:** the execution half of AC-08
**Depends on:** 6.5 (URL identity and scope), 6.4 (the per-origin `robots.txt` snapshot)

## 1. The decision that comes first — the crawler must not starve monitoring

A crawl is a burst of hundreds of requests against one host. Availability monitoring is a steady
trickle of requests that have a **cadence to meet**: a check that runs late is a check that reports
an outage late. Put both on one queue and the crawl wins, because it is simply bigger.

A politeness convention — "the crawler yields" — is not a control. It is an intention held in code
that nothing enforces and no test can observe. Isolation here therefore means three separate
budgets, each of which a test can saturate:

### 1.1 Its own durable work kind

`DurableWorkKinds.CrawlRun`, alongside `HttpCheck` and `SslCheck`. Crawl work is never queued as a
check and never dispatched by the monitoring dispatcher.

### 1.2 Its own Hangfire queue and worker pool

The `crawl` queue is served by a **second Hangfire server** with its own `WorkerCount`. Monitoring's
server does not list the `crawl` queue at all, so a crawl cannot occupy a monitoring worker even
when every crawl worker is busy — and a crawl that runs for its full duration limit occupies exactly
one crawl worker for that time.

Sharing one server with a queue priority list would not do this. Hangfire's queue ordering decides
which job a **free** worker picks up next; it does not reserve workers. Ten long crawls on a shared
pool of four workers starve the monitoring queue completely, whatever order the queues are listed
in.

### 1.3 Its own outbound-request budget, strictly below the shared HTTP budget

Every crawl request goes through the same `ISafeHttpTransport` as every other outbound request, and
so through the same global concurrency limiter. That limiter is the third place a crawl could
starve monitoring: filling all twenty global slots would block checks at the transport rather than
at the queue.

So a run holds at most `CrawlSchedulingOptions.RequestConcurrency` requests in flight, and options
validation refuses a value above **half** the transport's global concurrency. The crawler can never
hold more than half the shared budget, whatever else is running.

### 1.4 How this is verified

By test, not by inspection:

- a saturated crawl leaves at least half the global transport budget free
  (`CrawlIsolationTests`);
- a crawl whose every request stalls for longer than a monitoring cadence does not delay a
  concurrent monitoring-shaped request beyond that cadence.

## 2. Politeness (BR-L05, BR-L09)

- **Per-host rate limit**, default 2 requests/second/host, applied before the request and separately
  from concurrency. Concurrency bounds how many are in flight; the rate limit bounds how fast they
  start. A host needs both.
- **User agent** from `Monitoring:HttpTransport:UserAgent`, the same identifier every other outbound
  request carries, so a client's logs show one recognisable agent (BR-L09).

## 3. Limits and stopping (BR-L05)

| Limit | Where enforced | Stop reason when it binds |
|---|---|---|
| Maximum pages | `CrawlFrontier` (6.5) | `PageLimit` |
| Maximum depth | `CrawlFrontier` (6.5) | not a stop; deeper links are checked, not followed |
| Maximum duration | execution loop, against `TimeProvider` | `DurationLimit` |
| Request concurrency | execution loop | not a stop |
| Per-host rate | `HostRequestRateLimiter` | not a stop |

A run that drains its frontier stops with `FrontierExhausted` — the **only** reason that means the
site was covered. A run that stopped on a budget says so, and 6.8 must never render it as a clean
result.

## 4. Cancellation preserves what was found (BR-L10)

Results are written to the sink **as each target resolves**, never batched to the end. Cancellation
then needs no special path to preserve findings: everything already resolved is already recorded.

The run is marked `Cancelled` with stop reason `Cancelled`. It is never `Completed`. A partial crawl
that reported "no broken links" as a completed run would be worse than no crawl at all.

The final flush still runs on the cancellation path, so a target that was discovered but never
reached is recorded as `Unknown` with skip reason `RunStopped` rather than vanishing. It is
deliberately not `Healthy` and deliberately not absent — "nobody looked at this" has to be visible.

## 5. Source-target reporting (BR-L07)

A target is **fetched once** — that is 6.5's revisit rule — but it may be **linked from many pages**,
and "which page contains the broken link" is what makes the report actionable (AC-08).

`CrawlLinkLedger` separates the two: it collects the source pages that point at each target, and
emits one result per distinct source-target pair once that target's outcome is known. Pairs are
deduplicated, so a page linking to the same broken URL five times contributes one result and one
affected page, not five.

Order does not matter to the ledger. A source discovered after its target was already fetched emits
immediately; a target fetched after several sources pointed at it emits one result for each.

## 6. Robots (BR-L02)

Every internal fetch is checked against the origin's stored `robots.txt` snapshot from 6.4 — the
crawl performs no robots fetch of its own. An origin with no snapshot is crawled, for the same
reason 6.4's rules raise no finding without one: absence of evidence is not evidence.

**Overriding robots is granted only when all three hold:**

1. the run asked for it;
2. the target is **non-production** — a production crawl never bypasses published restrictions;
3. the origin carries an **approved exception** on its `robots_snapshot` row, with the reason and
   approver 6.4 already records.

The granted-or-refused decision, and the reason it was refused, are recorded on the run outcome. An
override that left no trace would be exactly the silent flag this project refuses to have.

## 7. SSRF and authorization (BR-L01, and the Phase 0 network policy)

Every request — internal page, external link, redirect hop — goes through `ISafeHttpTransport`, and
therefore through the actual-connection destination policy and the per-endpoint target
authorization evidence. There is no second HTTP client and no bypass.

That has a consequence worth stating plainly: **an external link is only fetched when the project
holds target-authorization evidence for its host and port.** Everything else is recorded as
`Skipped`, with reason `TargetNotAuthorized` when no evidence covers the host and
`ExternalCheckDisabled` when the run did not opt in at all. BR-L08 allows external targets to be
checked; it does
not make the crawler a general-purpose fetcher for whatever host a remote page names, and following
an arbitrary `href` through our own network position is exactly the SSRF the policy exists to
prevent. External checking is also off by default per run.

## 8. HTML is read and discarded (BR-E10)

Link extraction uses the same AngleSharp parser 6.2 uses, in the same inert configuration — no
browsing context, no requester, nothing that can fetch what the document references. It returns a
list of `href` strings and nothing else. The document does not reach the sink, the run record, or a
log.

## 9. What this increment does not do

No schema, no persistence, no comparison between runs, no views. The results go to
`ICrawlResultSink`, which 6.7 implements against the crawl tables it owns; the in-memory
implementation registered now is what the execution tests assert against.

## 10. Evidence

| Rule | Where it lives | Tests |
|---|---|---|
| BR-L02 robots and override authorization | `CrawlRobotsGate`, `CrawlRobotsReader` | `CrawlRobotsGateTests`, `CrawlExecutionTests` |
| BR-L05 duration, concurrency, per-host rate | `CrawlRun`, `HostRequestRateLimiter` | `CrawlExecutionTests`, `HostRequestRateLimiterTests` |
| BR-L06 classification from transport facts | `CrawlRun.Observe` | `CrawlExecutionTests` |
| BR-L07 source-target pairs, deduplicated | `CrawlLinkLedger` | `CrawlLinkLedgerTests`, `CrawlExecutionTests` |
| BR-L08 external checked, never explored | `CrawlFrontier`, `CrawlRun` | `CrawlExecutionTests` |
| BR-L09 identifiable user agent | shared `SafeHttpTransportOptions.UserAgent` | covered by the transport's own tests |
| BR-L10 cancellation preserves findings | `CrawlRun`, `CrawlLinkLedger.Flush` | `CrawlExecutionTests`, `CrawlLinkLedgerTests` |
| Isolation (section 1) | queue, work kind, request budget | `CrawlIsolationTests` |

AC-08's own sentence — "a crawl stays within scope, respects limits and reports a broken internal
link with its source page" — is
`CrawlExecutionTests.ExecuteAsync_ReportsABrokenInternalLinkWithItsSourcePage`.

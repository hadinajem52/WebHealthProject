# Phase 6 — Proposed Increment Plan

**Goal:** advanced recurring maintenance, technical SEO checks, and a safe bounded crawler.
**Acceptance criteria:** AC-07, AC-08; AC-09 regression-tested.
**Plan estimate:** 10–15 working days. This proposal totals **13 days** across eight increments.

Each increment is a vertical, demonstrable slice (Delivery Principle 1), and each names the decision
to settle **before** code — the pattern that worked in Phase 5, where writing the certificate
boundary rule down first made AC-06 a test rather than an argument.

---



## 6.1 — Recurring maintenance occurrences, timezone-aware (2 days)

Regression-test the Phase 4 minimum behaviour first, then expand recurrence into occurrences.

**Decide first — daylight saving.** A window at 02:30 local recurs into a day where 02:30 does not
exist (spring forward) and into a day where it happens twice (autumn back). Both need a written rule
— skip, shift, or run both — before any expansion code, because the tests are exactly those two days
and the comparison direction has to be a decision rather than an accident.

Expansion must be **idempotent**: keyed on (window, occurrence start) so re-running the expander
cannot double-book a window, and so a horizon can be extended without rewriting history. Occurrences
are already materialised rows, which is the right shape — suppression during a check must never
depend on recomputing a recurrence.

Covers BR-M05 and the AC-09 regression.

## 6.2 — SEO value extraction from the body already read (1.5 days)

Runs only for successful HTML responses, and reuses the bounded body `SafeHttpTransport` already
captures — no second fetch, no new network surface.

**Decide first — the parser.** Regular expressions over untrusted HTML are a correctness and
denial-of-service trap; a real parser (AngleSharp) is a new dependency and needs the supported-version
selection Phase 0 requires. Choose and record before writing extraction.

**The privacy rule is the hard constraint:** extract values, never retain HTML. Title, meta
description, canonical and robots-meta values are stored with their lengths; the document is not, and
must not reach logs, diagnostics or audit data either. That deserves its own test asserting absence,
not just presence of the extracted values.

## 6.3 — Canonical and `noindex` policy (1 day)

Canonical validity, uniqueness and expected host; production versus non-production `noindex` policy.

Findings go through the existing finding and issue-key machinery with their own rule keys, exactly as
the performance rules did in 5.4 — so SEO incidents deduplicate and resolve on the same path, and
BR-I04 independence holds without new plumbing.

Half of AC-07.

## 6.4 — `robots.txt` and sitemap at the origin root (1.5 days)

**Decide first — robots is per origin, not per endpoint.** Fifty endpoints on one host must produce
one fetch, cached with a TTL. Modelling it per endpoint is the mistake that makes this both slow and
rude to the target.

Correct group and comment parsing, longest-match precedence, and the wildcard `Disallow: /` check.
The fetch is bounded and goes through the same actual-connection SSRF policy as every other outbound
request. Configured sitemap availability, and recorded policy exceptions, complete AC-07.

## 6.5 — Crawl scope and URL identity (1.5 days, pure domain)

No I/O. Seeds, allowed hosts and path prefixes, fragment removal, tracking-parameter stripping,
revisit prevention, and query-string explosion caps — all pure functions.

**Decide first — the canonical crawl URL.** This string *is* the revisit key. If it normalises too
little the crawler loops; too much and it misses real pages. Getting it wrong is not a performance
bug, it is an unbounded crawl of someone else's site.

This is the highest-value, lowest-cost test surface in the phase, for the same reason the SSL
boundary rules were in 5.3: every rule is a pure function with exact inputs and outputs.

## 6.6 — Bounded crawl execution (2.5 days)

Page, depth, duration, concurrency and per-host rate limits; partial results preserved on
cancellation; external links checked without recursing into them.

**Decide first — isolation.** Crawler work must not starve availability checks. That means its own
durable work kind and its own worker budget, not a shared queue with a politeness convention. The
verification is a test that runs a crawl and proves scheduled checks still meet their cadence — a
claim that cannot be made by inspection.

Every request goes through the actual-connection SSRF control; robots overrides are restricted to
authorized, owned, non-production targets and are recorded.

## 6.7 — Crawl schema, results, and comparison (1.5 days)

Migration for crawl runs and link results, with source-target uniqueness within a run.

**Apply the Phase 5 lesson directly.** Link results are the highest-volume table in this phase.
Reporting will filter by run and by classification, so those columns belong **on the result row** with
a composite index from the first migration — not reached through a join to the run. Phase 5 lost time
to exactly that shape, where the filter predicate and the window predicate lived on different tables
and no index could serve them. Capture query plans against seeded data **before** shipping the views,
not after.

New, continuing and resolved broken-link comparison between runs, and a written retention rule for
crawl data.

## 6.8 — Views, authorization, and the phase gate (1.5 days)

Maintenance, SEO and broken-link views in the established dashboard style, every filter server-side,
every role's direct requests tested. Gate evidence for AC-07, AC-08 and the AC-09 regression.

---

## Sequencing and risk

6.1 is independent and can run first or in parallel. 6.2 → 6.3 are strictly ordered. 6.4 is
independent of 6.2/6.3. 6.5 must precede 6.6. 6.7 must precede 6.8.

| Risk | Where | Mitigation |
|---|---|---|
| Unbounded or looping crawl | 6.5 | URL identity settled and unit-tested before any fetching |
| Crawler starves monitoring | 6.6 | Separate work kind and budget; proven by test, not by design intent |
| SSRF through a new fetch path | 6.4, 6.6 | Reuse the existing actual-connection policy; no new HTTP client |
| HTML or sensitive body retained | 6.2 | Absence test, covering logs and diagnostics as well as tables |
| Reporting rework | 6.7 | Filter columns on the high-volume row; plans captured before the UI |
| New dependency | 6.2 | Supported-version selection recorded before adoption |

## What this plan deliberately excludes

Long-term crawl retention *enforcement*, daily aggregates, and any production rollout. Retention is
defined here as a rule and enforced in Phase 7, which owns retention — the same boundary Phase 5 drew
when it declined to build rollups it could not size against a retention policy that does not exist
yet.

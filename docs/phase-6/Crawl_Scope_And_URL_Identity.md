# Crawl scope and URL identity (BR-L01, BR-L03, BR-L04)

**Work item:** Phase 6.5
**Acceptance contribution:** the scope half of AC-08; the precondition for 6.6

Everything in this increment is a pure function. No I/O, no clock, no database. That is deliberate:
the rules below decide whether the crawler terminates, and a rule that can only be observed by
running a crawl against a live site is a rule nobody can test.

## 1. The decision that comes first — what the canonical crawl URL is

The canonical crawl URL is the **revisit key**. Two links that produce the same canonical URL are
the same page and the second one is never fetched; two links that produce different canonical URLs
are different pages and both are.

Both directions of error are real:

- **Normalise too little** and every anchor, every `?utm_source=`, every session token becomes a new
  page. The frontier grows faster than it drains and the crawl does not terminate — against
  someone else's site.
- **Normalise too much** and genuinely distinct pages collapse onto one key. The crawl finishes,
  reports clean, and has never looked at most of the site.

The first failure is the dangerous one, so where the two are in tension this design prefers to
collapse. Where collapsing would be wrong on an ordinary site, it does not.

### 1.1 The rules

Applied in order. Steps 1-6 are the same transformation `EndpointUrlNormalizer` already applies to
endpoint identity, so a URL cannot mean one thing to a monitor and another to a crawl.

| # | Rule | Why |
|---|---|---|
| 1 | Absolute `http`/`https` only; anything else is not a crawl target | `mailto:`, `tel:`, `javascript:` and `data:` are links a page may contain and a crawler must never dereference |
| 2 | Reject user information (`user:pass@`) | Credentials in a link are not identity, and following one would send them |
| 3 | Lowercase scheme; IDNA-ASCII host, lowercased, trailing dot removed | `HTTP://Example.COM.` and `http://example.com` are one host |
| 4 | Remove the default port (`:80` on http, `:443` on https) | Same reason |
| 5 | Empty path becomes `/`; dot segments resolved | `/a/../b` and `/b` are one resource |
| 6 | Decode percent-encoded unreserved characters; uppercase the escapes that remain | `%7Ea` and `~a` are one path |
| 7 | **Fragment removed** | BR-L03. `#top` is a position in a document, not a document |
| 8 | Query handled by the run's policy (section 2) | BR-L04 |
| 9 | Path case and the trailing slash are **preserved** | See 1.2 |

### 1.2 What is deliberately *not* normalised

**Path case.** `/About` and `/about` stay distinct. Most origins serve paths case-sensitively; on a
case-insensitive one this costs at most one duplicate fetch of a page, which the page cap already
bounds. Folding case would be the "misses real pages" error, and it would be silent.

**The trailing slash.** `/docs` and `/docs/` stay distinct for the same reason — they are commonly
two resources, one of which redirects to the other. The redirect is itself the evidence, and 6.6
records it as a `Redirected` classification rather than hiding it.

**`index.html`.** `/` and `/index.html` stay distinct. Stripping a default document name is a guess
about the server's configuration, and a wrong guess drops a page.

**The order of a preserved query.** Only the `Canonicalize` policy sorts (section 2).

## 2. Query-string policy (BR-L04)

Configurable per run, because sites differ and no single answer is right for all of them.

| Policy | Behaviour |
|---|---|
| `Canonicalize` (**default**) | Drop tracking parameters, then sort the survivors by name and then value |
| `PreserveOrder` | Drop tracking parameters, keep the authored order |
| `Ignore` | Drop the query entirely; the path alone is the key |

Sorting is the default because `?a=1&b=2` and `?b=2&a=1` are one page on essentially every site, and
treating them as two is a combinatorial route to a non-terminating crawl. `PreserveOrder` exists for
the minority of applications where parameter order is meaningful.

`Ignore` is the aggressive option and is off by default: on a paginated site it would collapse every
page of a listing onto the first.

### 2.1 Tracking parameters

Removed by default (BR-L04): the `utm_*` family, `gclid`, `fbclid`, `msclkid`, `dclid`, `yclid`,
`igshid`, `mc_eid`, `mc_cid`, `_ga`, `_gl`, `ref` and `referrer`. They are set by the *referrer*,
never by the resource, so two URLs differing only in them are one page. Names are matched
case-insensitively, and the `utm_` match is a prefix so a new member of the family needs no change.

The set is a run-level input rather than a constant, so a site that genuinely uses `ref` as a route
parameter can say so.

### 2.2 The explosion caps

Tracking-parameter removal is not enough on its own. A faceted catalogue generates unbounded query
variants from parameters that are not tracking parameters at all, and each one is legitimately a
distinct page by the rules in 1.1.

Two caps bound it, and both are reported as a skip reason rather than applied silently:

- **Parameters per URL** (default 12). A URL carrying more is skipped; it is a generated
  permutation, not a page anyone links to on purpose.
- **Query variants per path** (default 32). Once one path has produced that many distinct canonical
  URLs, further variants of that path are skipped. The first 32 are still crawled, so a paginated
  section is covered; a facet grid is cut off at a bound.

Both caps are per run and per path, so one exploding section cannot consume the page budget of the
whole site.

## 3. Scope (BR-L01)

A crawl starts only from its configured seeds and recurses only inside its scope. Every discovered
URL gets exactly one of three decisions:

A URL that is not a crawl target at all never reaches this decision: `CrawlUrlNormalizer` rejects it
first, with one of the reasons in section 1.1, and a rejected href is never dereferenced. What
remains is a canonical http(s) URL, and it is either in scope or out of it:

| Decision | Meaning | What 6.6 does with it |
|---|---|---|
| `Internal` | Host is allowed **and** the path matches an allowed prefix | Fetch it, and follow its links |
| `External` | Any other canonical http(s) URL | Check its status, do **not** parse or follow it (BR-L08) |

**Allowed hosts are exact matches on the normalized host,** with an explicit opt-in for subdomains
per host entry. A crawl of `example.com` that silently included `anything.example.com` would leave
the scope its operator configured, and on a site with user-controlled subdomains that is unbounded.

**Allowed path prefixes default to the seeds' own directories.** A crawl seeded at
`https://host/docs/` stays under `/docs/`. Prefix matching is on the normalized path and is
case-sensitive for the same reason 1.2 preserves path case. An empty prefix list means the whole
host.

The seed itself is subject to the scope check. A seed outside its own allowed hosts is a
configuration error, rejected when the run is validated rather than discovered halfway through.

## 4. Revisit prevention (BR-L03)

The frontier holds one entry per canonical URL for the life of the run. A URL that has been admitted
is never admitted again, whatever depth it is rediscovered at and however many pages link to it.

Depth is recorded as the depth of the **first** admission, which is the shortest path from a seed,
because the frontier is drained in the order it was filled. That matters: taking the last discovery
instead would let a deep rediscovery push a shallow page beyond the depth limit and drop it.

Seeds are depth 0. A link found on a depth-*n* page is depth *n+1*. The depth limit bounds what is
**followed**, not what is recorded — a link discovered at the limit is still checked, it just does
not contribute its own links.

## 5. What this increment does not do

No fetching, no HTML parsing, no persistence, no rate limiting. Those are 6.6. The boundary is
exactly the one that makes the rules above unit-testable: this increment turns text into decisions,
and 6.6 acts on the decisions.

## 6. Evidence

| Rule | Where it lives | Tests |
|---|---|---|
| BR-L03 identity, fragments, revisit | `CrawlUrlNormalizer`, `CrawlFrontier` | `CrawlUrlNormalizerTests`, `CrawlFrontierTests` |
| BR-L04 query policy, tracking, caps | `CrawlUrlNormalizer`, `CrawlFrontier` | `CrawlUrlNormalizerTests`, `CrawlFrontierTests` |
| BR-L01 seeds, hosts, path prefixes | `CrawlScope` | `CrawlScopeTests` |
| BR-L05 page and depth budgets | `CrawlFrontier` | `CrawlFrontierTests` |
| BR-L06 classification | `CrawlLinkClassifier` | `CrawlLinkClassifierTests` |

`CrawlFrontierTests.Frontier_TerminatesOnASiteWhereEveryPageLinksToEveryOther` is the termination
proof: a fully connected twenty-page site with anchor variations on every link is drained in
twenty-one fetches.

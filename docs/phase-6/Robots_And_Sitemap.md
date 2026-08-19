# `robots.txt` and sitemap at the origin root (BR-E06, BR-E07, BR-E08)

**Work item:** Phase 6.4
**Acceptance contribution:** completes AC-07 together with 6.3

## 1. The decision that comes first — robots belongs to an origin, not an endpoint

`robots.txt` lives at the **origin root**. For `https://host/a/b` the file is
`https://host/robots.txt`, never `https://host/a/robots.txt` — that is BR-E06, and it is also what
makes the modelling decision obvious once it is written down.

Fifty endpoints on one host share **one** `robots.txt`. Modelling the fetch per endpoint would mean
fifty requests for one file, repeated every refresh, at a target we do not own. That is both slow
and rude, and it is the mistake this increment exists to avoid.

So the cache key is the **origin** — scheme, host and effective port — and one row in
`robots_snapshot` serves every endpoint under it. A refresh fetches once per origin per TTL, no
matter how many endpoints depend on it.

### 1.1 Fetching never happens on the check path

The refresh is a recurring job, not part of finalising a check. Two reasons:

- an availability check must not wait on a second host's response before its own result can be
  written;
- fifty checks a minute against one origin must not become fifty conditional fetches with a
  race to populate the cache.

Rules are therefore evaluated against the **stored snapshot**, exactly as 6.2's rules are evaluated
against a stored extraction. A check whose origin has no snapshot yet raises no robots findings at
all — absence of evidence is not evidence, and the first refresh is at most one TTL away.

**An expired snapshot is not evidence either.** The check path requires `expires_at > now`, so a
refresh that is delayed or switched off stops robots findings rather than continuing to assert a
policy nobody has re-read. A cache that outlives its TTL on the read path is not a cache; it is
stale data wearing a timestamp.

### 1.2 One fetch per origin is enforced, not hoped for

Two workers can see the same origin as due. The refresh therefore **claims** an origin before
fetching: a conditional update moves its expiry forward, and only the worker whose update changed a
row proceeds; a missing row is claimed by inserting it, where the primary key settles the race. A
worker that loses the claim skips the origin, because the fetch it would have made is the one
already happening.

### 1.3 The representative endpoint must itself be authorized

The transport authorises against an endpoint, and an origin has many. The representative is the
earliest endpoint carrying **current, unrevoked target authorization** for that host and port — not
an arbitrary one, which could hand the fetch an unauthorized context and skip an origin the project
is entitled to read. An origin with no authorized endpoint is not fetched at all: nothing authorises
it.

### 1.4 The fetch reuses the existing outbound path

`robots.txt` goes through `ISafeHttpTransport` like every other outbound request: the same
actual-connection SSRF control, the same destination policy, the same bounded body, the same
user agent. There is no second HTTP client and no new network surface — the same rule 6.2 followed
for the page body.

The body is capped at 512 KB on the wire, which is the limit search engines document for
`robots.txt`, and **the stored bound is the same number**. Storing less than was fetched would mean
judging a policy from a prefix of it — a `Disallow: /` past the storage cap would read as "no
restrictions", which is precisely backwards. `robots.txt` is a public policy document, not page
content, so retaining it does not touch BR-E10; it is bounded anyway, because an unbounded column
fed by a remote host is a mistake regardless of what the content is.

A body that hits the read cap is recorded as `Unavailable`, not `Fetched`. An incomplete policy
cannot be judged, and the incompleteness has to be visible rather than silently favourable.

## 2. Parsing

The parser is a pure function over text: no I/O, no clock, no database. It is the highest-value test
surface in this increment for the same reason 6.5's URL identity will be.

### 2.1 Group structure and comments

- A `#` starts a comment and runs to end of line, wherever it appears.
- Consecutive `User-agent:` lines form **one** group with several agents; the group's rules are the
  `Allow`/`Disallow` lines that follow, up to the next `User-agent:` after at least one rule line.
- A rule line before any `User-agent:` belongs to no group and is ignored.
- Unknown directives are ignored rather than treated as errors — the file is written by someone
  else, and a strict parser would fail on the common case.
- Directive names are case-insensitive; **paths are case-sensitive**, because URLs are.

### 2.2 Which group applies

The group whose agent list contains the most specific match for our user agent wins; `*` is the
fallback group and is used only when no specific group matches. Agent matching is
case-insensitive and by prefix, which is what the convention actually is.

### 2.3 Longest match wins, `Allow` breaks ties

For a given path, every rule in the applicable group is tested by prefix. The rule with the
**longest** pattern wins. When an `Allow` and a `Disallow` of equal length both match, `Allow`
wins — the documented convention, and the safer direction: a tie resolved towards "blocked" would
make us report a site as unindexable when search engines would crawl it.

An empty `Disallow:` value means "nothing is disallowed" and is not a match. `Allow:` with an empty
value is ignored.

`*` and `$` wildcards are supported: `*` matches any run of characters, `$` anchors the end of the
path. They are compiled to an explicit matcher rather than to a regular expression — the pattern
comes from a remote host, and handing an attacker-supplied pattern to a backtracking engine inside
the monitoring worker is the same trap 6.2 rejected for HTML.

## 3. The rules

| Rule key | Rule | Fires when | Severity |
|---|---|---|---|
| `Seo.RobotsBlocksSite` | BR-E07 | the applicable group disallows the site root (`Disallow: /` with no narrower `Allow`) | production `Critical`, else Warning |
| `Seo.RobotsBlocksEndpoint` | BR-E07 | the endpoint's own path is disallowed while the root is not | production `High`, else Warning |
| `Seo.RobotsUnavailable` | BR-E06 | the origin's `robots.txt` could not be fetched or returned 5xx | Warning |
| `Seo.SitemapMissing` | BR-E08 | a sitemap is required for the origin and none is reachable | Warning |

`Seo.RobotsBlocksSite` is the only `Critical` in the SEO family. A production site that tells every
crawler to go away is not a misconfiguration to look at next sprint; it is the whole site
disappearing from search, which is the loss BR-E07 is about.

### 3.1 A 404 is a valid answer

No `robots.txt` means nothing is disallowed. A 404 is recorded as a successful refresh with an empty
rule set, and raises no finding. `Seo.RobotsUnavailable` is for a fetch that failed or a server
error — an origin that cannot answer, which is a different fact from an origin that answers "no
restrictions".

### 3.2 A recorded exception suppresses BR-E07

An origin may legitimately be blocked — a staging host, or a site deliberately withdrawn. The
snapshot carries `exception_reason` and `exception_approved_by_user_id`; when set, the blocking
rules are recorded as expected and no finding is raised. This is the same shape as the endpoint's
HTTP exception: an approval with a reason and an approver, not a silent flag.

That approval is set through `IRobotsPolicyService`, which is authorized like every other registry
mutation, validates its input, uses optimistic concurrency, and writes an audit record. Origin
policy that could only be changed by editing the database by hand would not be a decision anyone
could audit — and a rule that can never be enabled is not implemented. **Clearing the reason clears
the approval with it**, so an exception can never outlive the reason it was granted for.

The management **view** for this policy lands in 6.8 with the other SEO views; the increment
delivers the authorized path, its validation and its audit trail.

### 3.3 Findings carry bounded values

A canonical href or a `Disallow` pattern comes from a remote host and can be arbitrarily long, while
a finding's observed and expected columns are bounded. Both are bounded when the finding is built,
not at the column: a hostile value that failed the save would roll back the entire check result,
turning an SEO detail into lost availability history.

### 3.4 Findings are per endpoint even though the fetch is per origin

Fifty endpoints behind one blocking `robots.txt` are fifty endpoints that are actually blocked, so
each raises its own finding against its own monitor and tracks its own incident. The deduplication
that matters — not fetching fifty times — happens at the fetch, which is where the cost is.

## 4. Sitemaps (BR-E08)

Sitemap URLs come from two places: `Sitemap:` directives in `robots.txt`, which are absolute by
specification and belong to the origin, and a per-origin configured URL. Availability is checked by
the same refresh job, with the same bounded, SSRF-controlled transport, and only the **status** is
recorded — never the sitemap body, which can be large and is not evidence of anything a status code
does not already carry.

A sitemap is only *required* when the origin says it is (`sitemap_required`), because most origins
do not have to have one and a finding on every origin would be noise.

## 5. Verification

Unit (`RobotsTxtParserTests`, pure):

- comments stripped everywhere, including mid-line and inside values;
- consecutive `User-agent:` lines forming one group; a new group starting only after a rule line;
- rules before any `User-agent:` ignored;
- specific agent beating `*`, and `*` used only as fallback;
- longest match winning, and `Allow` winning an equal-length tie;
- empty `Disallow:` meaning nothing is disallowed;
- `*` and `$` wildcards, including `$` anchoring;
- case-insensitive directives and agents, case-sensitive paths;
- unknown directives ignored; malformed lines ignored rather than throwing;
- `Sitemap:` directives collected regardless of group.

Integration:

- one fetch per origin, not per endpoint, with the TTL honoured;
- a 404 recorded as "no restrictions" and raising nothing;
- a truncated fetch recorded as `Unavailable` rather than as a permissive policy;
- an expired snapshot producing no findings until it is refreshed;
- a recorded exception suppressing BR-E07, set through the authorized service and audited;
- the configured transport user agent selecting the group written for it;
- a byte-order mark on the first line not costing the first group;
- finding values bounded so a hostile pattern cannot fail the save.

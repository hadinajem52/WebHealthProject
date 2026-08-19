# SEO value extraction (BR-E01, BR-E02, BR-E03, BR-E10)

**Work item:** Phase 6.2
**Rules:** BR-E01 and BR-E10 in full; the facts BR-E02/BR-E03/BR-E04 are judged on
**Acceptance contribution:** half of AC-07 (the policy half is 6.3)

Extraction runs on the body `SafeHttpTransport` has **already** read, inside the existing bounded
response cap. There is no second fetch, no new HTTP client, and no new outbound surface.

## 1. The decision that comes first — the parser

Regular expressions over untrusted HTML were rejected outright. They are wrong on exactly the input
that matters (unquoted attributes, comments, `<script>` content, malformed nesting) and a
backtracking pattern over an attacker-controlled document is a denial-of-service vector inside the
monitoring worker.

A hand-written scanner was rejected too. Restricting it to `<head>` does not avoid tokenising
comments, CDATA, quoting and implicit-close rules; it only moves the correctness risk from a
maintained library into this repository.

**Selected: AngleSharp `1.7.1`** (MIT). Its parser follows the HTML5 tree-construction spec, which
is the right semantics for the question these checks ask — *what does a search engine see on this
page?* — and it recovers from malformed markup the way a browser does.

### 1.1 Supported-version selection (Phase 0 section 3)

| Fact | Value |
|---|---|
| Package | `AngleSharp` `1.7.1` |
| License | MIT |
| Transitive dependencies on `net10.0` | **none** — verified with `dotnet list package --include-transitive` against a probe project |
| Framework fit | compatible with the pinned `net10.0` target |
| Vulnerability status | `dotnet list package --vulnerable --include-transitive` reports none |

The version is pinned centrally in `Directory.Packages.props`, referenced only by
`WebHealth.Infrastructure`, and locked in that project's `packages.lock.json`. A zero-dependency
parser is what makes this acceptable at all: it adds one audited assembly rather than a graph.

### 1.2 The parser is never given network access

Parsing uses `AngleSharp.Html.Parser.HtmlParser` directly against an in-memory document. No
`IBrowsingContext` with a requester is constructed, so the parser cannot fetch stylesheets, scripts,
images, or anything else a document references. The document is inert markup, not a page being
loaded.

## 2. The privacy rule is the hard constraint (BR-E10)

**Values are extracted; the document is never retained.** The response body exists only as the
`ReadOnlyMemory<byte>` the transport already holds, and only for the duration of one parse.

What is stored is title, meta description, canonical href and robots-meta content, each with its
**observed length** and **element count**. Nothing else from the document is stored, and the
document must not reach logs, diagnostics, audit payloads, or findings either:

- `seo_observation` has no column that can hold markup;
- `check_result.safe_diagnostic` is derived from the failure category, never from body content;
- findings carry a rule key and a bounded observed value, never a document fragment;
- the extractor takes no logger, so there is no path from a parsed document to a log sink — and a
  parse that throws is turned into a recorded reason rather than an exception carrying document
  text up the stack.

This is asserted twice as **absence**, not merely by tests that check the extracted values are
present. Once at the extractor, where a document whose body, comments, scripts and unrelated
metadata all carry a distinctive marker must return nothing containing it. Once at the database,
where a real check finalises that document and every text column of `seo_observation`,
`check_result`, `finding` and `audit_event` is scanned for the marker — by enumerating
`information_schema`, so a column added later is covered automatically rather than quietly falling
outside the claim.

### 2.1 Stored values are bounded, lengths are not

| Value | Stored at most | Length column |
|---|---|---|
| title | 512 chars | observed length, untruncated |
| meta description | 1024 chars | observed length, untruncated |
| canonical href | 2048 chars | observed length, untruncated |
| robots meta | 256 chars | observed length, untruncated |

Storing the length separately is what keeps truncation honest: a 4000-character meta description is
recorded as 4000 characters long even though only the first 1024 are kept, so BR-E03 is judged on
the real value and not on a silently shortened one.

## 3. Applicability (BR-E01)

Extraction is attempted only when **all** of the following hold; otherwise the observation is
recorded as `NotApplicable` with a reason, which is what "non-HTML content is marked Not
Applicable" requires — an explicit recorded decision, not a missing row.

| Condition | Reason recorded when it fails |
|---|---|
| the exchange succeeded | `TransportFailed` |
| the status is 2xx | `NonSuccessStatus` |
| the media type is `text/html` or `application/xhtml+xml` | `NonHtml` |
| the body is non-empty | `EmptyBody` |
| the document parses | `ExtractionFailed` |

`ExtractionFailed` exists because extraction must never be able to cost a check. The parser runs on
untrusted input on the path that finalises availability results, so a parse that throws is recorded
as a decision and the availability result is written exactly as it would have been.

Media type is compared case-insensitively after stripping parameters, so `TEXT/HTML; charset=utf-8`
is HTML. A response with no `Content-Type` at all is treated as `NonHtml`: guessing that unlabelled
bytes are markup is exactly the "parse binary content" BR-E01 forbids.

### 3.1 Character encoding

The `Content-Type` charset is authoritative when the response declares one and it can be resolved.
Otherwise the bytes are handed to AngleSharp, which performs the spec's own sniffing (BOM, then
`<meta charset>`) and falls back to UTF-8 when the document declares nothing at all. An
unrecognised charset name is treated as absent rather than as a failure — the document is still
readable, and refusing to look at it would lose the check.

**`CodePagesEncodingProvider` is registered.** .NET ships only the UTF encodings, ASCII and
Latin-1; `windows-1252` — still common on exactly the older sites these checks exist for — would
otherwise fail to resolve, and the document would be decoded as UTF-8 into replacement characters.
The provider is part of the shared framework on `net10.0`, so this costs no dependency. It is worth
testing with bytes that decode *differently* under UTF-8 and windows-1252 (`0x93`/`0x94`), because a
sample that happens to be valid Latin-1 passes whether or not the declared charset was honoured.

### 3.2 A truncated document is extracted but flagged

The body cap can cut a document mid-way. `<head>` almost always survives, so extraction still runs,
but the observation records `document_truncated`. **6.3 must not raise a "missing" finding from a
truncated document** — absence of a canonical tag in a document that was cut short is not evidence
that the page lacks one. Presence-based findings stay valid; absence-based ones do not.

## 4. What is extracted

| Column group | Meaning |
|---|---|
| `title`, `title_length`, `title_count` | first non-empty `<title>` text, whitespace-collapsed; count of title elements, so BR-E02 can distinguish missing from duplicate |
| `meta_description`, `meta_description_length`, `meta_description_count` | `<meta name="description">` content and how many were present (BR-E03) |
| `canonical_href`, `canonical_length`, `canonical_count`, `canonical_absolute_url` | `<link rel="canonical">` href exactly as authored, how many were present, and the absolute form resolved against the response's final URL (BR-E04 needs both: the authored value for diagnosis, the resolved one for the host comparison) |

Three rules keep the canonical honest:

- **Values are read from `<head>` only.** An SVG `<title>` in the body is a graphic's label and a
  `<meta>` outside `<head>` is ignored by search engines; counting either would make BR-E02 and
  BR-E03 judge something that is not page metadata.
- **Resolution uses the authored href in full, before bounding.** Bounding first would resolve a
  long canonical from a truncated prefix and record a host the page never named.
- **A resolved URL past the stored bound is recorded as absent, never truncated.** A cut-off URL
  names a different resource, and storing one would hand 6.3 a host comparison against something
  the page never pointed at. The authored value and its real length are still recorded.

The canonical href is bounded by **trimming only** — no whitespace collapsing. It is diagnostic
evidence, and internal whitespace in a URL is precisely the authoring mistake worth seeing. Title,
description and robots content are human-readable text and are collapsed.

The base for resolution is the transport's **redacted** final URL, which carries no query string.
The only case that changes is an empty canonical href, which resolves without the query. That is
the accepted cost of not carrying query strings — which can hold secrets — into storage.
| `robots_meta`, `robots_meta_length`, `robots_meta_count` | the content of **every** `<meta name="robots">` on the page, combined into one lowercased directive list, for BR-E05 — the directives are cumulative, so keeping only the first would read `index` on a page that also says `noindex` |

Resolving the canonical href is a *fact* about the document plus the URL it was served from, so it
belongs here. Whether that resolved host is acceptable is *policy*, and belongs to 6.3.

## 4.1 Extraction runs outside the finalization transaction

**The trade-off, stated plainly.** Extraction happens before the transaction opens, which means it
also happens for a command that turns out to be a duplicate or invalid — work that is then thrown
away. That was chosen over the alternative: parsing inside the transaction would put an untrusted
document of arbitrary shape inside the window where the logical-check row is locked. A wasted parse
on a rare duplicate finalization is bounded and blocks nobody; lock time on the busiest path in the
system is a cost every check pays.

Parsing depends only on the evidence in hand, so it happens **before** the transaction opens.
Running an untrusted document of arbitrary size and shape through a tree builder while the
logical-check row is locked would put page complexity inside the most contended path in the system.
Combined with `ExtractionFailed`, this means SEO work can neither block nor roll back the
availability result it rides along with.

A check whose evidence never produced a response still records a decision (`TransportFailed`), so
non-applicability is visible in the history for every HTTP check rather than only for those that
got as far as a response. Certificate checks have no page at all and record nothing.

## 5. Storage shape

`seo_observation` is keyed by `logical_check_id`, one row per check that produced a decision,
exactly like `certificate_observation`. It carries `endpoint_monitor_id` on the row itself through
the composite foreign key to `logical_check (id, endpoint_monitor_id)` — the Phase 5 reporting
lesson: the filter column belongs on the high-volume row, not one join away.

Check constraints enforce the applicability contract in the database: a `NotApplicable` row carries
a recognised reason, no values, **zero counts and zero lengths**; an `Applicable` row carries no
reason; every length is at least the length of the value stored beside it, so a bounded value can
never claim to be shorter than what is kept.

## 6. Verification

Unit (`SeoExtractionRuleTests`, pure domain):

- applicability for each failing condition and the reason it records;
- media-type parsing with parameters, casing, and a missing header;
- whitespace collapsing, bounding, and that observed length is the untruncated length.

Extractor (`SeoValueExtractorTests`, no database):

- title, description, canonical and robots extraction from well-formed markup;
- duplicate titles, canonicals and descriptions produce counts greater than one;
- malformed markup (unclosed tags, a comment containing markup, a `<title>` inside `<script>`)
  extracts what a browser would and does not throw;
- head scoping: an SVG `<title>` and body-level `<meta>`/`<link>` are not page metadata;
- the declared charset is honoured for bytes that would decode differently without it;
- a canonical longer than the stored bound resolves from the full authored value and records no
  resolved URL rather than a truncated one;
- a document the parser cannot handle produces `ExtractionFailed` rather than an exception;
- a truncated document still extracts and is flagged;
- **absence test:** a document containing a distinctive marker produces no extracted value
  containing it.

Database (`DatabaseFoundationAssertions`):

- the column inventory of `seo_observation` — no column exists that could hold markup;
- each half of the applicability contract is rejected by name;
- **absence test at the persistence boundary:** a finalised check over a marker-laden document
  leaves the marker in no text column of `seo_observation`, `check_result`, `finding` or
  `audit_event`.

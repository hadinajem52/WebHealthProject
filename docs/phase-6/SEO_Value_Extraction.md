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
- the extractor takes no logger, so there is no path from a parsed document to a log sink.

This is asserted by a test that checks for **absence** — that a document containing a distinctive
marker string produces no stored value, diagnostic or audit payload containing it — not merely by
tests that check the extracted values are present.

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

Media type is compared case-insensitively after stripping parameters, so `TEXT/HTML; charset=utf-8`
is HTML. A response with no `Content-Type` at all is treated as `NonHtml`: guessing that unlabelled
bytes are markup is exactly the "parse binary content" BR-E01 forbids.

### 3.1 Character encoding

The `Content-Type` charset is authoritative when the response declares one and .NET recognises it.
Otherwise the bytes are handed to AngleSharp, which performs the spec's own sniffing (BOM, then
`<meta charset>`). An unrecognised charset name is treated as absent rather than as a failure — the
document is still readable, and refusing to look at it would lose the check.

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
| `robots_meta`, `robots_meta_length`, `robots_meta_count` | `<meta name="robots">` content, lowercased, for BR-E05 |

Resolving the canonical href is a *fact* about the document plus the URL it was served from, so it
belongs here. Whether that resolved host is acceptable is *policy*, and belongs to 6.3.

## 5. Storage shape

`seo_observation` is keyed by `logical_check_id`, one row per check that produced a decision,
exactly like `certificate_observation`. It carries `endpoint_monitor_id` on the row itself through
the composite foreign key to `logical_check (id, endpoint_monitor_id)` — the Phase 5 reporting
lesson: the filter column belongs on the high-volume row, not one join away.

Check constraints enforce the applicability contract in the database: a `NotApplicable` row carries
a reason and no values, an `Applicable` row carries no reason, and every length and count is
non-negative.

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
- a truncated document still extracts and is flagged;
- **absence test:** a document containing a distinctive marker produces no extracted value
  containing it.

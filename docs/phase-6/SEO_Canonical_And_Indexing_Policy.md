# Canonical and indexing policy (BR-E02, BR-E03, BR-E04, BR-E05, BR-E09)

**Work item:** Phase 6.3
**Depends on:** 6.2, which extracts the facts these rules judge
**Acceptance contribution:** half of AC-07

6.2 recorded *what the page says*. This increment decides *whether that is acceptable*, and does so
through the finding and issue-key machinery that already exists — exactly as the performance rules
did in 5.4. No new incident plumbing, no new notification path.

## 1. The decision that comes first — where the policy is read from

Every other rule the normalizer applies is read from the check's **configuration snapshot**, which
is fingerprinted: a check whose request-shaping policy does not match its fingerprint is rejected
before it can be finalised. SEO policy is deliberately **not** put there.

Two reasons, and both matter:

- **The fingerprint governs what the request was, not how the response is judged.** Timeout,
  redirect limit, body cap and accepted statuses change the exchange itself, which is why a
  mismatch means the evidence cannot be trusted. An expected canonical host changes nothing about
  the request that was made.
- **Adding fields to the fingerprint invalidates every existing monitor.** Phase 5 paid that cost
  once for certificate identity, with a migration that recomputed fingerprints in SQL and a test
  proving the backfilled value matched what the application computes. Paying it again for a rule
  that is evaluated exactly once — when the finding row is written — buys nothing.

So SEO policy is read at finalization from the **endpoint and its environment**, and the inputs
actually used are written onto the observation row — `policy_expected_host`,
`policy_indexing_expectation` (already resolved, so `Default` is never ambiguous later) and
`policy_description_required`. A finding records only what failed; without these columns a *passing*
observation could never be explained, and a policy edited afterwards would silently rewrite the
meaning of stored history. Changes to the policy itself appear in the endpoint's audit trail
alongside every other change to how a target is evaluated.

Because a finding is written once and never recomputed, this is equivalent to snapshotting for every
purpose except re-judging history, which the system does not do.

## 2. Per-endpoint policy

Three settings on the endpoint, all with a documented default so an endpoint that configures
nothing still gets the rule the specification asks for.

| Setting | Default | Meaning |
|---|---|---|
| `seo_expected_canonical_host` | the endpoint's own normalized host | BR-E04's "expected host" comparison |
| `seo_indexing_expectation` | `Default` | `Indexable`, `NoIndex`, or environment-derived |
| `seo_description_required` | `true` | BR-E03's "unless the endpoint disables this rule" |

### 2.1 The indexing expectation is one three-valued setting, not two flags

BR-E05 (production must not be `noindex`) and BR-E09 (non-production may be *required* to be
`noindex`) are the same question with the answer reversed, so they are one setting rather than two
independent booleans that can contradict each other.

`Default` resolves from the environment:

| Environment | Resolved expectation | Rule |
|---|---|---|
| production | `Indexable` | BR-E05: a production page carrying `noindex` is a finding |
| non-production | `NoIndex` | BR-E09: a publicly indexable staging page is a finding |

An endpoint that is genuinely meant to be `noindex` in production — a print view, a thank-you page —
sets `NoIndex` explicitly, which is what BR-E05's "explicitly expected for that endpoint" means. A
staging endpoint that must stay indexable sets `Indexable`.

### 2.2 Severity

**An unmet expectation on production is `High`; everywhere else it is `Warning`.** Nothing here is
`Critical`: a page with the wrong canonical is misconfigured, not unreachable, and reserving
`Critical` for availability keeps the severity vocabulary meaningful. `High` still maps to a
`Warning` *outcome*, as it already does for certificate expiry — the escalated urgency travels with
the finding and its incident, not with the availability signal.

## 3. The rules

Every rule has its own rule key and therefore its own issue key, so a page with a bad canonical and
a page that is unreachable track as independent incidents (BR-I04) and resolve independently.

| Rule key | Rule | Fires when | Severity |
|---|---|---|---|
| `Seo.TitleMissing` | BR-E02 | no non-empty `<title>` in head | Warning |
| `Seo.TitleDuplicate` | BR-E02 | more than one `<title>` element | Warning |
| `Seo.DescriptionMissing` | BR-E03 | no non-empty meta description, and the endpoint requires one | Warning |
| `Seo.CanonicalNotAbsolute` | BR-E04 | a canonical is present but authored as a relative reference | Warning |
| `Seo.CanonicalInvalid` | BR-E04 | a canonical is present but does not resolve to an `http`/`https` URL | Warning |
| `Seo.CanonicalDuplicate` | BR-E04 | more than one canonical link | Warning |
| `Seo.CanonicalUnexpectedHost` | BR-E04 | the resolved canonical host is not the expected host | production `High`, else Warning |
| `Seo.NoIndexUnexpected` | BR-E05 | the page is `noindex` where the expectation is `Indexable` | production `High`, else Warning |
| `Seo.IndexableUnexpected` | BR-E09 | the page is not `noindex` where the expectation is `NoIndex` | production `High`, else Warning |

### 3.1 A missing canonical is not a finding

BR-E04 governs canonicals that exist: absolute, valid, unique, expected host. It does not require
one. Raising a finding for every page without a canonical would fire on most of a healthy site and
teach operators to ignore SEO findings — which is the failure mode that makes a rule worse than no
rule. If a canonical becomes mandatory for some endpoint, that is an endpoint setting, and it is not
in this increment.

### 3.2 Every robots meta on the page counts

A page can carry more than one `<meta name="robots">`. The directives are **cumulative**: a page
whose first tag says `index, follow` and whose second says `noindex` is `noindex`. Extraction
therefore combines all of them into one directive list rather than keeping the first, because
"first tag wins" would call that page indexable.

### 3.3 `noindex` is read from the directive list, not by substring

The robots meta content is a comma-separated token list. A page is `noindex` when a token is
`noindex` or `none`; `none` is the shorthand for `noindex, nofollow` and skipping it would let the
strongest possible directive pass unnoticed. Tokens are compared after trimming, and the value was
already lowercased at extraction. A substring search would match `noindexing` and would miss
nothing useful in exchange.

Only the meta tag is read. The `X-Robots-Tag` **header** carries the same directives and is not
extracted yet, so a page suppressed by header alone is not detected here — a stated limitation,
recorded rather than silently assumed away.

### 3.4 Absence rules are suppressed on a truncated document

`Seo.TitleMissing`, `Seo.DescriptionMissing` and `Seo.IndexableUnexpected` all conclude something
from what the document does *not* contain. A body that hit the response cap may simply not include
the part that would have contained it, so these are suppressed when the observation records
`document_truncated`. Presence-based rules — duplicate title, unexpected host, unexpected
`noindex` — stay valid, because what was seen was really there.

This is the rule 6.2 wrote down for 6.3 to honour, and it is asserted directly.

### 3.5 A canonical element with an empty href is invalid, not absent

`<link rel="canonical" href="">` states a canonical and states nothing usable. That is the invalid
case BR-E04 is about, so it raises `Seo.CanonicalInvalid`. Only a page with **no** canonical element
at all is silent, and silence is not a finding (§3.1).

### 3.6 Nothing fires on a non-applicable observation

If BR-E01 said the response was not HTML, not successful, empty, or unparseable, there are no facts
to judge and no findings are produced. The `NotApplicable` reason is the record of why.

## 4. Verification

Unit (`SeoRuleEvaluatorTests`):

- each rule in isolation, on its trigger and just off it;
- production versus non-production severity for all three environment-sensitive rules;
- the `Default` expectation resolving both ways, and an explicit expectation overriding it;
- expected-host defaulting to the endpoint host, and an override being honoured;
- `none` treated as `noindex`, and `noindexing` not treated as `noindex`;
- absence rules suppressed on a truncated document while presence rules still fire;
- no findings at all for a `NotApplicable` observation;
- every rule key distinct, so no two SEO rules share an issue key;
- an empty canonical href raising `Seo.CanonicalInvalid`;
- finding values bounded, so a remote value long enough to overflow the stored column cannot fail
  the save and roll back the whole check result.

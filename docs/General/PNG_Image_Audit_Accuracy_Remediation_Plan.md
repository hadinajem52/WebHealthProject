# PNG Image Audit — Comparison Accuracy Remediation Plan

**Repository:** `hadinajem52/WebHealthProject`
**Current baseline:** `b43c9fa132875e116b8e91296684d932abd3bbd4`
**Audited baseline:** `cb149a317e98e9db6b19718044c891856685920b`
**Suggested path:** `docs/General/PNG_Image_Audit_Accuracy_Remediation_Plan.md`
**Status:** **Ready for implementation**
**Feature:** `Tools → PNG image audit`
**Supersedes:** section 4 (Format-comparison policy) and section 15 (Legal result-state matrix) of
[PNG_Image_Audit_Implementation_Plan.md](PNG_Image_Audit_Implementation_Plan.md)

---

# 1. Goal

The PNG image audit crawls, discovers, fetches, and measures correctly. Its **format-comparison
subsystem** does not. This plan replaces that subsystem so that every recommendation it emits is
false-positive-free against a stated, reproducible contract.

Discovery, transport, identification, and transparency measurement are **not** changed by this plan
beyond what section 6 requires for reporting.

---

# 2. Evidence

Measured against `https://www.audicapital.com/` on 2026-08-28, run
`01a04736-063d-778f-b083-7cc217c5b9fb`, cross-checked with an independent Python/Pillow reference
implementation using libwebp.

| Fact | Value |
| --- | --- |
| Unique images discovered | 124 (reference agreed exactly) |
| PNGs found | 15 (reference agreed exactly) |
| Opaque PNGs | 11 |
| Transparent PNGs | 4 |
| WebP candidates reported | **4** |
| WebP candidates verified by reference | **15** |
| Savings surfaced | ~37 KB |
| Savings measured by reference | ~3.45 MB |

## 2.1 Confirmed defects

1. **ImageSharp is not a usable WebP size oracle.** ImageSharp `3.1.12` at
   `WebpFileFormatType.Lossless`, `Quality = 100`, `WebpEncodingMethod.BestQuality` produced files
   **1.08×–2.17×** larger than libwebp on the same pixels. Normalized-PNG byte counts agreed to
   within 0.1%, isolating the divergence to the WebP path. Seven images were reported as *negative*
   savings (as low as −57%) that genuinely save 33–47%.

2. **Transparency terminates analysis instead of routing it.**
   [PngImageAnalyzer.cs:191](../../src/WebHealth.Infrastructure/PngAudits/PngImageAnalyzer.cs#L191)
   returns `PngAnalysisResult.Transparent()` on the first pixel with `A < 255`. All four transparent
   PNGs save 31.2%–35.6% as RGBA lossless WebP, verified pixel-identical. Three of the four are
   visually opaque — rounded-corner anti-aliasing at alpha 241/242, every affected pixel within 3px
   of an edge, distributed evenly across four quadrants.

3. **The normalized-PNG baseline is a strawman.**
   [PngImageAnalyzer.cs:282](../../src/WebHealth.Infrastructure/PngAudits/PngImageAnalyzer.cs#L282)
   forces `PngColorType.Rgb`. A palette or grayscale source is inflated into RGB, making the second
   gate trivially passable. Not exercised by this site; a real false-positive channel elsewhere.

4. **16-bit rejection is correct behavior, reported as a failure.** WebP lossless stores 8-bit ARGB.
   A 16-bit PNG cannot round-trip losslessly. The rejection stays; only the reporting changes.

## 2.2 Non-defects, recorded to prevent regression

* An RGBA container whose alpha values are all 255 **is** already compared. Three such images on
  this site were handled correctly. The defect is pixel-level, not container-level.
* The savings arithmetic has never been wrong. The database check constraints enforce it.

---

# 3. The accuracy contract

> A **Verified lossless WebP candidate** means a pinned libwebp-backed encoder produced an actual
> WebP file; that file carries a lossless `VP8L` payload; it decodes to identical dimensions and
> byte-exact 8-bit RGBA values including hidden RGB under fully transparent pixels; rendering-
> relevant colour information is preserved or provably equivalent; and it passed the configured
> thresholds against both the current file and a pinned lossless-optimized PNG reference.

Anything failing any condition receives **no format-change recommendation**.

## 3.1 Two claims, earned separately

| Claim | Requires |
| --- | --- |
| "WebP is smaller than your current file" | verified candidate + threshold vs original |
| "You should change format" | the above **plus** threshold vs optimized-PNG reference |

Phase 2 earns the first. Phase 3 earns the second. The UI must not conflate them.

## 3.2 Fidelity mode

The strict profile uses `exact` (preserve RGB under `A = 0`). This **reduces** measured savings
relative to the section 2 figures, which were taken with Pillow's default `exact=False`. The Audi
Tadawul logo has 9,305 fully transparent pixels; its 35.6% is an upper bound. A looser
"rendering-equivalent" mode may be added later but must never be mixed into the strict result.

## 3.3 Metadata and colour policy

Symmetrical, or the comparison is invalid:

```text
Non-rendering metadata (EXIF, XMP)  → removed from BOTH candidates
Rendering-relevant colour           → preserved or normalized equivalently on BOTH
```

`cwebp -metadata all` paired with `oxipng --strip safe` is **not** a valid pairing — it inflates the
WebP with bytes the PNG reference discarded, manufacturing false negatives.

PNG expresses colour through `sRGB`, `gAMA`, `cHRM`, `cICP`, and `iCCP`. WebP carries ICC, EXIF, and
XMP only. Decision table:

| Source chunks | Action |
| --- | --- |
| none | sRGB assumed — proceed |
| `sRGB` | proceed |
| `iCCP` | carry the ICC profile, verify preserved |
| `gAMA` / `cHRM` / `cICP` without `sRGB` or `iCCP` | `ColorProfileUnsupported`, no recommendation |

[PngChunkInspector](../../src/WebHealth.Infrastructure/PngAudits/PngChunkInspector.cs) currently
reads only `IHDR`, `acTL`, `IDAT`, `IEND`. Colour-chunk detection is new work, bounded to a chunk
scan plus this table. It is **not** a colour-management layer.

## 3.4 Verification independence

An encoder that also verifies itself can agree with its own bug. The **acceptance corpus** must be
verified through a decoding path independent of the encoder. Per-run production verification may
reuse the encoder's library, because the corpus is what establishes that library's trustworthiness.

## 3.5 ImageSharp is removed from the recommendation path

ImageSharp is retained for decode, preflight, and fact gathering. It is not used as a size oracle,
a correction-factor base, or a prefilter in either direction. A negative prefilter preserves the
false negatives being removed; a positive prefilter saves nothing, because every image it passes
must still be encoded and verified by the real engine.

---

# 4. Target model

## 4.1 Facts — always recorded when measurable

```text
Width, Height, FrameCount, PixelCount
BitDepth, ColorType
TransparentPixelCount, UsesTransparency
```

Transparency is a **fact**, never a terminal outcome.

## 4.2 Classifications

```csharp
public enum PngImageAnalysisClassification
{
    NotPng,
    IdentificationFailed,
    DimensionsExceeded,
    PixelLimitExceeded,
    DecodedMemoryExceeded,
    AnimatedPng,
    DecodeFailed,
    HighBitDepthPng,
    ColorProfileUnsupported,
    ComparisonUnavailable,
    VerifiedWebpCandidate,
    OptimizedPngPreferred,
    BelowWebpThreshold
}
```

Retired: `UsesTransparency`, `WebpComparisonFailed`, `OpaqueWebpCandidate`,
`OpaqueBelowWebpThreshold`, `UnsupportedBitDepth`.

## 4.3 Recommendations

```csharp
public enum PngRecommendation { None, OptimizePng, LosslessWebp }
```

A transparent image may legally be `UsesTransparency = true` **and**
`Classification = VerifiedWebpCandidate` **and** `Recommendation = LosslessWebp`.

## 4.4 Comparison metrics

```csharp
public sealed record PngComparisonMetrics(
    long OriginalPngBytes,
    long OptimizedPngBytes,
    long VerifiedWebpBytes)
{
    public long ReferencePngBytes => Math.Min(OriginalPngBytes, OptimizedPngBytes);
}
```

The gate becomes `MeetsThreshold(ReferencePngBytes, VerifiedWebpBytes)`. Beating the minimum is
equivalent to beating both, and names the quantity honestly.

## 4.5 Decision algorithm

```csharp
if (!isPng) return NotPng;

var preflight = InspectPng();
if (preflight.ExceedsResourceLimits) return ResourceLimitOutcome;
if (preflight.IsAnimated) return AnimatedPng(facts);

var source = preflight.BitDepth == 16 ? DecodeAsRgba64() : DecodeAsRgba32();
var facts = MeasureTransparency(source);

if (preflight.BitDepth == 16) return HighBitDepthPng(facts);
if (!CanPreserveColorMeaning(preflight)) return ColorProfileUnsupported(facts);

var webp = await engine.CreateVerifiedWebpAsync(originalBytes);
if (!webp.Verified) return ComparisonUnavailable(facts);
if (!thresholds.MeetsThreshold(originalBytes.Length, webp.Bytes.Length))
    return BelowWebpThreshold(facts, webp);

var optimized = await engine.CreateVerifiedOptimizedPngAsync(originalBytes);
if (!optimized.Verified) return ComparisonUnavailable(facts, webp);

var reference = Math.Min(originalBytes.Length, optimized.Bytes.Length);
if (thresholds.MeetsThreshold(reference, webp.Bytes.Length))
    return VerifiedWebpCandidate(facts, optimized, webp);

return optimized.Bytes.Length < originalBytes.Length
    ? OptimizedPngPreferred(facts, optimized, webp)
    : BelowWebpThreshold(facts, optimized, webp);
```

The 16-bit path decodes to `Rgba64`; its decoded-memory budget must use **8 bytes per pixel**, not
the current fixed `DecodedBytesPerPixel = 4`.

---

# 5. Schema impact

**The current model is enforced by the database, not only by C#.** Five check constraints on
`png_audit_image_result` encode it:

| Constraint | Pins |
| --- | --- |
| `ck_..._state` | the transparent ⇒ no-comparison ⇒ no-recommendation rule, per classification |
| `ck_..._classification` | the exact 15 classification names |
| `ck_..._recommendation` | `None` \| `LosslessWebp` only, with `suggested_format = 'WebP'` |
| `ck_..._comparison` | `normalized_png_bytes` and the exact savings arithmetic |
| `ck_..._transport` | enumerates classifications again |

`ck_..._state` contains the branch:

```sql
(classification = 'UsesTransparency' AND frame_count = 1 AND uses_transparency IS TRUE
 AND normalized_png_bytes IS NULL AND recommendation = 'None')
```

A transparent WebP candidate is physically unstorable today. **Phase 1 is a schema migration, not a
C# refactor.**

## 5.1 One migration, not four

Design the full target constraint set for Phases 1–4 and land it in a **single** migration in
Phase 1, even though the engine arrives in Phase 2. Three migrations each rewriting `ck_..._state`
means triple the fixture churn and three chances to write a `Down` that cannot run.

Column changes in that migration:

* `normalized_png_bytes` → `optimized_png_bytes`
* `normalized_savings_bytes` / `normalized_savings_percent` → `reference_savings_*`
* add `bit_depth`, `color_type`
* add `comparison_profile_version` if per-row provenance is wanted

The replacement constraints must be **as strict** as the originals for the new model. Their
strictness is why the savings arithmetic has never been wrong; do not loosen them to make room.

## 5.2 Test coupling (CLAUDE.md integration rules)

The same change must update, in
[DatabaseFoundationAssertions.cs](../../tests/WebHealth.IntegrationTests/Support/DatabaseFoundationAssertions.cs):
`ExpectedMigrations`, `ExpectedTables`, `TablesAddedAfterPhaseThree`, the per-table column
assertions, and every hand-written INSERT in a negative test.

The `Down` must run against data its `Up` allowed: it must retire rows using the new classification
and recommendation names **before** narrowing the constraints back.

---

# 6. Phased implementation

## Phase 0 — Contain the current output

Keep crawling, discovery, identification, and transparency measurement enabled. Stop presenting v1
comparison results as current truth.

* Bump `PngAnalysisProfiles.Comparison` to `libwebp-exact-vs-optimized-png-v2` and
  `PngAnalysisProfiles.Analyzer` to `png-alpha-v2` when Phase 2 lands; until then, branch the UI on
  the stored `ComparisonProfile`.
* Runs whose `ComparisonProfile` is `normalized-png-vs-lossless-webp-v1` render:
  `Legacy comparison profile — these savings were produced by the previous encoder and require a rerun.`
* Exclude legacy recommendation counts from any aggregate.
* Do **not** build a feature-flag framework, and do **not** lower thresholds to "fix" the totals.

Runs are already snapshotted with both profile values, so this is a conditional, not a subsystem.

**Gate:** no run displays a v1 savings figure without the legacy notice.

---

## Phase 1 — Separate facts from outcomes

Land the model change and the consolidated migration together.

* Introduce the section 4 classifications, recommendations, and metrics.
* Rewrite the five check constraints for the new model.
* Extend `PngChunkInspector` to read `sRGB`, `gAMA`, `cHRM`, `cICP`, `iCCP` and record bit depth and
  colour type as facts.
* Fix all **three** reader sites that assume transparency is terminal — this is the trap:
  * [PngAuditReader.cs:267](../../src/WebHealth.Infrastructure/PngAudits/PngAuditReader.cs#L267) —
    the filter must query `result.UsesTransparency == true`, not the classification
  * [PngAuditReader.cs:136](../../src/WebHealth.Infrastructure/PngAudits/PngAuditReader.cs#L136) —
    the transparency summary must count the persisted boolean
  * [PngAuditReader.cs:141](../../src/WebHealth.Infrastructure/PngAudits/PngAuditReader.cs#L141) —
    `pngsAnalyzed` is a **sum of mutually exclusive classifications** and breaks structurally the
    moment a transparent image can also be a candidate

All summary counts become fact-based:

```text
Uses transparency        → count where UsesTransparency == true
PNGs compared            → count where comparison completed
WebP candidates          → count where Recommendation == LosslessWebp
PNG optimization preferred → count where Recommendation == OptimizePng
```

**Gate:** `scripts\run-database-foundation-tests.ps1` green; the migration's `Down` runs against a
database populated with new-model rows; the transparency filter and summary agree with the persisted
boolean on a seeded fixture containing a transparent compared row.

---

## Phase 2 — Replace the WebP size oracle

Introduce the engine boundary:

```csharp
public interface IPngFormatComparisonEngine
{
    Task<PngFormatComparisonResult> CompareAsync(
        ReadOnlyMemory<byte> sourcePng,
        PngSourceEncodingFacts sourceFacts,
        PngRecommendationThresholds thresholds,
        CancellationToken cancellationToken);
}
```

`PngImageAnalyzer` keeps preflight, limits, decode, facts, and applicability. The engine owns tool
invocation, candidate generation, round-trip verification, byte measurement, and version reporting.

### 2a — Bounded spike (gate on Phase 2)

Evaluate Magick.NET against official `cwebp` on the 15-image corpus at equivalent settings
(`lossless`, `quality 100`, `method 6`, `exact`, identical metadata policy). Answer:

1. Does it produce byte-exact RGBA round trips?
2. Does it preserve the declared colour profile?
3. How close are its sizes to direct `cwebp`?
4. Identical behaviour on Windows and the deployment OS?
5. Can memory and CPU be bounded?
6. Is the package size acceptable?

Magick.NET is NuGet-delivered but still native code: it adds deployment size, codec attack surface,
and platform test burden. It is a convenience over loose binaries, not a managed-safety win.
SkiaSharp is **not** a candidate unless it demonstrably exposes exact-transparency, effort, and
metadata control.

```text
Spike passes            → Magick.NET is the first corrected engine
Leaves substantial bytes → pinned cwebp process, or a narrower libwebp binding
```

### 2b — Implement

Eligible: static 8-bit PNGs, opaque **and** transparent. Required per candidate:

1. payload is lossless `VP8L`
2. dimensions match
3. every decoded RGBA byte matches
4. hidden RGB under `A = 0` matches
5. colour policy satisfied
6. only then is its length used

Failure of any check ⇒ `ComparisonUnavailable`, `Recommendation = None`. **Never fall back to
ImageSharp** — results would depend on which instance ran the job.

If an out-of-process tool is chosen: no shell, fixed arguments never derived from URLs, private
random temp directory, per-process timeout, output-size cap, process-tree kill, concurrency 1,
`finally`-delete, non-zero exit ⇒ `ComparisonUnavailable`.

**Output at this phase:** `Verified lossless WebP size` and `Verified saving versus current file`.
The UI must **not** yet say "change format".

**Gate:** all 15 corpus images produce verified candidates or an explicit unavailable reason; zero
recommendations emitted without passing all six checks; acceptance corpus verified through an
independent decoder (section 3.4).

---

## Phase 3 — Add the optimized-PNG reference

Run a pinned lossless PNG optimizer against the **original encoded bytes** — not a re-encode from
decoded pixels, which is what made the current baseline invalid.

* `oxipng -o max --strip safe` with the section 3.3 symmetrical policy
* **No `--alpha`** — it alters hidden colours under transparent pixels and is documented as
  technically lossy
* Verify: decode independently, dimensions, exact RGBA, colour profile preserved
* Run **only after** the WebP candidate has already beaten the original-file threshold

Decision becomes the full four-way outcome from section 4.5. UI wording must be
`Beats the pinned optimized-PNG reference`, never `smallest possible format` — oxipng does not claim
global minimality.

**Gate:** a palette PNG and a grayscale PNG in the corpus produce `OptimizedPngPreferred` rather
than a WebP recommendation; no `LosslessWebp` recommendation exists without a verified optimized-PNG
reference.

---

## Phase 4 — 16-bit reporting

Behaviour is unchanged: **no lossless WebP recommendation.** Only the explanation changes.

```text
16-bit PNG
Transparency: used / not used
Lossless WebP comparison: not applicable because WebP stores 8-bit channels
```

Decode to `Rgba64`, count alpha accurately, budget 8 bytes per pixel.

**Gate:** a 16-bit RGBA fixture reports dimensions and transparency, classification
`HighBitDepthPng`, recommendation `None`.

---

# 7. Test corpus

Built per phase, not all at once.

| Phase | Fixtures added |
| --- | --- |
| 1 | palette 1/2/4/8-bit, palette + `tRNS`, grayscale 1/2/4/8-bit, grayscale-alpha, truecolor + `tRNS`, `sRGB`/`gAMA`/`cHRM`/`iCCP` variants |
| 2 | opaque RGB, opaque RGBA, single semitransparent pixel, fully transparent with non-zero hidden RGB, transparent logo artwork, metadata-heavy, ICC-profiled |
| 3 | palette and grayscale where an optimized PNG beats WebP |
| 4 | 16-bit RGB, 16-bit RGBA with transparency |
| all | APNG, truncated, corrupt |

Assertions for every emitted recommendation:

```text
candidate is VP8L
dimensions match
decoded RGBA matches exactly
hidden transparent RGB matches
required colour profile matches
beats original threshold
beats optimized-PNG threshold
```

Preserve the 15 audicapital images as a regression corpus — at minimum their SHA-256 hashes, source
characteristics, and expected classifications — so the 4-of-15 result cannot silently return.

Do not build fixtures exclusively with ImageSharp; that tests the analyzer against the ecosystem
that produced the blind spot.

---

# 8. Performance envelope

The current run spends ~47% of its time on 7 large opaque PNGs (31.5s average, 50.5s max). A
verified reference comparison costs more. Bound it:

1. skip comparison for animated and 16-bit images
2. skip when the original is too small to satisfy `MinSavingsBytes`
3. WebP first; optimized PNG only after WebP passes the original threshold
4. one comparison active at a time (matches the existing analysis semaphore and `WorkerCount = 1`)
5. per-image timeout
6. later, cache on `SHA-256(source bytes) + comparison profile + threshold profile`

`MaxDuration` is 30 minutes and already binds far before `MaxUniqueImages = 500`. Re-measure the
per-image budget after Phase 2 and revise `MaxDuration` or `ImageDecodeConcurrency` on evidence, not
in advance.

---

# 9. Out of scope

* Rewriting HTML, uploading images, or modifying the target site — unchanged from V1
* Persisting image binaries
* A looser "rendering-equivalent" fidelity mode (may follow; must not mix with strict results)
* Recalculating historical v1 runs — they require a rerun
* Animated PNG comparison
* A colour-management subsystem beyond the section 3.3 decision table

---

# 10. Expectation to set now

Under `exact` and symmetrical metadata, the section 2 figures will shrink. They were measured with
`exact=False`. Some of the 15 will fall below threshold, and images that beat the current file may
still lose to an optimized PNG and end as `OptimizedPngPreferred`.

That is the plan working, not a regression. The 15-of-15 result belongs to the claim *"WebP is
smaller than the current files."* The claim *"all 15 should change format"* is earned only in
Phase 3.

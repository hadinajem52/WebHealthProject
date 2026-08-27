# PNG Image Audit Tool — Implementation-Ready Plan

**Repository:** `hadinajem52/WebHealthProject`
**Reviewed baseline:** `fe6ca4a605fe766cebc2bd2d0e5df93d654bd4a8`
**Suggested path:** `docs/General/PNG_Image_Audit_Implementation_Plan.md`
**Status:** **Ready for implementation**
**Feature:** `Tools → PNG image audit`

---

# 1. Goal

Add a new **Tools** section to the sidebar, with **PNG image audit** as its first tool.

The tool will:

1. take an existing registered WebHealth endpoint;
2. crawl its internal HTML pages;
3. discover PNG image references;
4. safely fetch each unique image request;
5. inspect the actual image bytes and decoded pixels;
6. determine whether transparency is actually used;
7. for fully opaque PNGs, perform a real lossless WebP comparison;
8. report actionable conversion candidates;
9. persist run history and normalized results.

The feature is strictly **analysis-only**.

It must never:

* modify the target website;
* rewrite HTML;
* upload converted images;
* persist original or converted image binaries;
* make recommendations based only on `.png` filenames.

---

# 2. Final V1 product contract

## 2.1 What V1 audits

V1 discovers image references from:

```html
<img src="...">
<img srcset="...">

<picture>
    <source srcset="...">
    <img src="...">
</picture>
```

V1 does **not** inspect:

* CSS `background-image`;
* external stylesheets;
* custom lazy-loading attributes such as `data-src`;
* JavaScript-created image URLs;
* `blob:` images;
* inline `data:image/...`;
* browser-rendered DOM state.

Those belong in follow-on work.

---

## 2.2 V1 does not fetch arbitrary external/CDN origins

This is an intentional simplification from the previous plan.

The page crawler uses:

```text
Page traversal scope
    allowed hosts
    +
    allowed path prefixes
```

Image assets use:

```text
Image asset scope
    allowed hosts/origins only
    NO page path-prefix restriction
```

For example:

```text
Page:
https://example.com/application/page

Image:
https://example.com/assets/logo.png
```

The page remains constrained to `/application/`, but `/assets/logo.png` is valid because images do not inherit the page-path restriction.

Images on hosts outside the registered/explicitly allowed host set are recorded as:

```text
ExternalAssetHost
```

and are not fetched in V1.

This cleanly removes the previous external-robots ambiguity identified by the audit. 

External CDN auditing can later be added with its own origin allowlist and explicit robots policy.

---

# 3. PNG recommendation semantics

The tool must not say:

> no transparency = PNG is wrong

or:

> transparency = keep PNG

V1 knows substantially less than that.

## 3.1 Transparency rule

After decoding:

```text
UsesTransparency =
    at least one pixel has alpha < 255
```

Do not infer transparency from:

* MIME type;
* PNG color mode alone;
* existence of an alpha channel;
* filename;
* metadata.

Inspect the actual decoded pixels.

---

## 3.2 Result vocabulary

| Situation                           | Classification             | User-facing result                                                      |
| ----------------------------------- | -------------------------- | ----------------------------------------------------------------------- |
| Bytes are not PNG                   | `NotPng`                   | Downloaded resource is not actually PNG                                 |
| PNG uses 16-bit samples             | `UnsupportedBitDepth`      | 16-bit PNG — not eligible for the 8-bit V1 comparison                    |
| PNG has multiple frames             | `AnimatedPng`              | Animated PNG — no static conversion recommendation                      |
| Any pixel has alpha < 255           | `UsesTransparency`         | Uses transparency — not eligible for the opaque-PNG V1 recommendation   |
| Fully opaque, WebP threshold passes | `OpaqueWebpCandidate`      | Opaque PNG — lossless WebP candidate                                    |
| Fully opaque, threshold fails       | `OpaqueBelowWebpThreshold` | Opaque PNG — lossless WebP did not meet the configured saving threshold |
| Could not safely inspect            | `NotAnalyzed`              | Explain bounded reason                                                  |

This fixes the evidence-overclaiming problem in the first version. 

---

# 4. Format-comparison policy

For opaque PNGs, do a **real encoding comparison**, not an estimate.

However, comparing WebP directly against the original PNG can produce misleading results if WebP silently drops metadata.

V1 therefore uses two comparisons.

## Normalized baseline

From the same decoded pixels:

```text
Original PNG
     |
     v
decode
     |
     +--> normalized lossless PNG
     |
     `--> lossless WebP
```

Both comparison encodes use the same metadata policy:

```text
ComparisonMetadataPolicy = StripMetadata
```

The original file size is retained separately.

Persist:

```text
original_bytes
normalized_png_bytes
lossless_webp_bytes
```

A WebP recommendation requires WebP to pass the configured threshold against **both**:

```text
original PNG
AND
normalized PNG baseline
```

This means savings cannot be explained solely by:

* removable PNG metadata;
* a poorly optimized original PNG;
* format-specific metadata differences.

### Default threshold

```text
MinSavingsPercent = 10%
MinSavingsBytes   = 4 KB
```

Therefore:

```text
recommend WebP only if:

original_bytes - webp_bytes >= 4 KB
AND
(original_bytes - webp_bytes) / original_bytes >= 10%

AND

normalized_png_bytes - webp_bytes >= 4 KB
AND
(normalized_png_bytes - webp_bytes) / normalized_png_bytes >= 10%
```

Otherwise:

```text
OpaqueBelowWebpThreshold
```

---

# 5. Image library decision

Use **SixLabors.ImageSharp** behind:

```csharp
public interface IPngImageAnalyzer
{
    Task<PngAnalysisResult> AnalyzeAsync(
        ReadOnlyMemory<byte> encodedImage,
        CancellationToken cancellationToken = default);
}
```

Current ImageSharp documentation lists both PNG and WebP as built-in read/write formats and supports multi-frame/APNG workflows. ([docs.sixlabors.com][1])

Before merging the package change, record a short dependency-license review. Six Labors maintains specific commercial-use licensing rules, so this should be a deliberate dependency decision rather than an undocumented package addition. ([sixlabors.com][2])

Persist stable implementation profiles:

```text
analyzer_profile       = png-alpha-v1
comparison_profile     = normalized-png-vs-lossless-webp-v1
```

Historical results therefore remain interpretable after package or encoder upgrades.

---

# 6. URL handling contract

This is a critical design requirement.

An image URL must have three representations.

## `FetchUrl`

```text
Exact resolved absolute URL.
Only used in memory.

Query ordering and encoding are preserved.
Fragment is removed.
Sensitive parameters are NOT rewritten before requesting.
```

Never persist it for discovered image assets.

## `IdentityHash`

```text
SHA-256 of the discovered request identity.
```

The identity is based on the actual resolved request URL used for fetching, including meaningful query parameters.

This means:

```text
/image.png?w=400
/image.png?w=800
```

are two different image requests.

Likewise, signed CDN-style URLs must not be canonicalized in a way that invalidates their signature.

## `DisplayUrl`

```text
Bounded
+
sensitive-query-redacted
+
safe for persistence and UI
```

Also apply this distinction to redirect destinations.

Never:

* persist signed query secrets;
* log the raw `FetchUrl`;
* reorder query parameters used for fetching;
* use `DisplayUrl` for network requests.

The definition of deduplication is therefore:

> Every unique discovered **image request identity** is fetched at most once per run.

This directly incorporates the URL-contract issue from the audit. 

---

# 7. Shared crawling infrastructure decision

Do not invoke `CrawlExecutionService` from the PNG feature.

Also do not copy `CrawlRequestExecutor`.

Instead introduce a genuinely shared bounded-fetch layer.

Recommended namespace:

```text
WebHealth.Application.SiteAnalysis
WebHealth.Infrastructure.SiteAnalysis
```

## Contracts

```text
ISiteAnalysisFetcher
SiteAnalysisFetchRequest
SiteAnalysisFetchProfile
SiteAnalysisFetchResult
```

It owns only:

* `ISafeHttpTransport`;
* timeout handling;
* body limits;
* bounded retries;
* `Retry-After`;
* global/site-analysis request budgeting;
* per-host rate limiting;
* redirect-hop policies;
* failure normalization.

It knows nothing about:

```text
Broken links
PNG
CrawlLinkLedger
PngAuditResult
```

Architecture:

```text
                    ISafeHttpTransport
                           ^
                           |
                 ISiteAnalysisFetcher
                    /             \
                   /               \
       CrawlRequestExecutor       PngSiteCrawler
                  |                    |
          CrawlRunExecution     PNG-specific logic
```

Initially, `CrawlRequestExecutor` becomes a thin adapter over `ISiteAnalysisFetcher`.

That reduces regression risk.

---

# 8. Shared HTML discovery

Replace the current narrow parsing implementation internally with:

```csharp
public sealed record HtmlDocumentDiscovery(
    IReadOnlyList<string> NavigationHrefs,
    IReadOnlyList<HtmlImageReference> Images,
    string? BaseHref,
    bool NavigationFullyInspected,
    bool ImageReferencesFullyInspected);

public sealed record HtmlImageReference(
    string RawUrl,
    string AttributeKind,
    string? Descriptor);

public interface IHtmlDocumentDiscoveryExtractor
{
    HtmlDocumentDiscovery Extract(
        ReadOnlyMemory<byte> body,
        string? contentType);
}
```

Keep the existing:

```text
IHtmlLinkExtractor
```

temporarily as an adapter so the broken-links feature continues consuming only:

```text
NavigationHrefs
```

The shared implementation should live under `SiteAnalysis` or another neutral shared area, not under `PngAudits`.

The current broken-link extractor is explicitly link-oriented, so this refactor needs full regression coverage before PNG-specific work depends on it. 

---

# 9. Transport ceiling change

The current safe transport effectively caps response bodies at **2 MB**, so the previous proposed 8 MB PNG limit cannot work unchanged. 

Use the full V1 approach.

Change:

```text
MaxDecodedBodyBytes
```

into conceptually:

```text
DefaultMaxResponseBodyBytes  = 2 MB
AbsoluteMaxResponseBodyBytes = 8 MB
```

Existing HTTP monitors and broken-link crawls continue requesting their existing limits.

Only PNG image requests may request up to:

```text
8 MB
```

Tests must prove:

* default remains 2 MB;
* 8 MB requests are accepted when explicitly requested;
* > 8 MB is rejected before outbound execution;
* body truncation still works;
* existing monitor limits are unchanged;
* invalid PNG configuration fails application startup validation.

---

# 10. V1 resource limits

Use explicit configuration:

```text
PngAudits:
  Enabled: false

  WorkerCount: 1

  MaxPages: 200
  MaxDepth: 5

  MaxPageBytes: 1 MB
  MaxTotalPageBytes: 32 MB

  MaxImageReferencesPerPage: 1000
  MaxUniqueImages: 500
  MaxTotalImageSourceMappings: 5000

  MaxImageBytes: 8 MB
  MaxTotalImageBytes: 128 MB

  MaxWidth: 10000
  MaxHeight: 10000
  MaxDecodedPixels: 40000000
  MaxDecodedMemoryBytes: 256 MB

  MaxTotalHttpAttempts: 1500

  FetchTimeoutSeconds: 15
  TransientRetryCount: 1

  ImageFetchConcurrency: 1
  ImageDecodeConcurrency: 1

  MaxDuration: 30 minutes

  MinSavingsPercent: 10
  MinSavingsBytes: 4096
```

`MaxImageAnalysisDuration` may also be used as a stage deadline, but do not present it as a hard CPU kill if the underlying synchronous codec cannot be interrupted.

Hard safety comes from:

* encoded body limits;
* width/height limits;
* pixel limit;
* decoded-memory preflight;
* frame limits;
* single decode concurrency;
* encoder parallelism limits;
* overall run deadline.

The audit correctly pointed out that `MaxUniqueImages` alone does not bound potentially thousands of source mappings. 

---

# 11. HTTP and CPU fairness

A separate Hangfire queue is necessary but not sufficient.

The repository currently deliberately keeps crawling from taking all global outbound capacity, so PNG audits must preserve that property. 

For V1:

```text
Existing site-analysis parent request budget
    max = existing crawler budget

Broken-link crawler
    may continue using its existing concurrency

PNG audit
    additional child gate = 1 concurrent outbound request
```

Therefore a PNG run cannot monopolize the shared budget.

Also:

```text
PNG Hangfire workers        = 1
ImageFetchConcurrency       = 1
ImageDecodeConcurrency      = 1
Image encoder parallelism   = 1
```

Process flow should be:

```text
fetch image
    ->
analyze
    ->
encode comparison
    ->
persist
    ->
dispose
    ->
next image
```

Do not download many images into memory before decoding them.

---

# 12. Run lifecycle

Use the PageAudit pattern, not the existing broken-link `Running-before-enqueue` pattern.

The current PageAudit implementation already claims `Queued` runs into `Running` with lease tokens and lease expirations, and reconciliation re-enqueues stale queued or expired running work.

PNG statuses:

```text
Queued
Running
Completed
CompletedWithWarnings
Failed
Cancelled
```

Fields:

```text
queued_at
started_at
updated_at
finished_at

attempt_count

lease_token
lease_expires_at
```

Active uniqueness:

```sql
UNIQUE(endpoint_id)
WHERE status IN ('Queued', 'Running')
```

### Opening a run

```text
user clicks Run
    |
    v
authorize
    |
IEndpointTestGate
    |
validate endpoint
    |
create immutable Queued snapshot
    |
commit
    |
enqueue
```

If enqueue fails:

```text
Queued -> Failed
failure_code = WorkerUnavailable
```

### Worker claim

Atomically:

```text
Queued
    ->
Running

lease_token = new GUID
lease_expires_at = now + lease duration
started_at = now
attempt_count += 1
```

### Long-running heartbeat

Unlike a PageSpeed provider call, the PNG audit may run for tens of minutes.

Extend the lease:

* after a page batch;
* after an image-result batch;
* at least once per configured heartbeat interval.

Results are committed only while the worker still owns the lease.

---

# 13. Immutable run configuration

Every run snapshots everything required for reproducibility.

Persist:

```text
endpoint_id

seed_url_snapshot
is_production_snapshot

allowed_page_hosts
allowed_page_path_prefixes
allowed_asset_hosts

query_policy

max_pages
max_depth
max_page_bytes
max_total_page_bytes

max_image_references_per_page
max_unique_images
max_total_image_source_mappings

max_image_bytes
max_total_image_bytes

max_width
max_height
max_decoded_pixels
max_decoded_memory_bytes

max_total_http_attempts

fetch_timeout_seconds
transient_retry_count

min_savings_percent
min_savings_bytes

analyzer_profile
comparison_profile

queued_at
```

The worker reconstructs the job from this snapshot.

Do **not** load current configuration midway through a run.

Before executing, recheck:

* endpoint still exists;
* endpoint remains testable;
* endpoint URL still equals the snapshotted target.

If the endpoint URL changed:

```text
Failed
TargetChanged
```

rather than auditing one URL while labeling results as another endpoint.

---

# 14. Persistence model

Use five tables.

## `png_audit_run`

Stores lifecycle, immutable configuration and summary counts.

Important coverage fields:

```text
crawl_coverage_limited
image_analysis_coverage_limited
source_mapping_coverage_limited
```

Do not collapse all three into one boolean.

---

## `png_audit_image_result`

One row per unique discovered image request identity.

Fields include:

```text
id
run_id

image_display_url
image_identity_hash

final_display_url
final_identity_hash

declared_content_type
detected_format

http_status_code
response_bytes

width
height
frame_count
pixel_count

uses_transparency                 nullable; unknown for APNG
transparent_pixel_count           nullable; unknown for APNG
transparent_pixel_percent         nullable; unknown for APNG

classification
reason_code

recommendation
suggested_format

normalized_png_bytes
candidate_webp_bytes

original_savings_bytes
original_savings_percent

normalized_savings_bytes
normalized_savings_percent

recorded_at
```

Savings values are **signed**.

A WebP larger than PNG therefore legitimately produces a negative saving.

---

## `png_audit_image_source`

One row per:

```text
image
+
source page
+
attribute type
+
srcset descriptor
```

Fields:

```text
image_result_id

source_page_display_url
source_page_identity_hash

attribute_kind
descriptor
```

---

## `png_audit_discovery_skip`

For values that cannot become normalized image results:

```text
data:
blob:
unsupported scheme
malformed URL
overlong value
external asset host
reference limit
```

Fields:

```text
run_id

source_page_display_url
source_page_identity_hash

attribute_kind
descriptor

bounded_safe_raw_value
reason_code

recorded_at
```

Sensitive query values must be redacted from `bounded_safe_raw_value`.

---

## `png_audit_coverage_reason`

Stores multiple coverage-loss reasons.

```text
run_id

area
    Crawl
    ImageAnalysis
    SourceMappings

reason_code
count
```

Examples:

```text
PageLimit
DepthLimit
PageBodyTruncated
NavigationReferenceLimit
ImageReferenceLimit
UniqueImageLimit
TotalPageBytesLimit
TotalImageBytesLimit
SourceMappingLimit
RobotsDisallowed
DurationLimit
```

This fixes the persistence mismatch identified in the audit. 

---

# 15. Legal result-state matrix

Define this **before writing the migration**.

An image result can be:

```text
FetchFailed
HttpNonSuccess
ResponseTruncated
NotPng
IdentificationFailed
UnsupportedBitDepth
DimensionsExceeded
PixelLimitExceeded
DecodedMemoryExceeded
AnimatedPng
DecodeFailed
UsesTransparency
WebpComparisonFailed
OpaqueWebpCandidate
OpaqueBelowWebpThreshold
```

Every state should have database-checkable invariants.

For example:

```text
UsesTransparency
    => uses_transparency = true

OpaqueWebpCandidate
    => uses_transparency = false
    => normalized_png_bytes IS NOT NULL
    => candidate_webp_bytes IS NOT NULL
    => recommendation = 'LosslessWebp'

AnimatedPng
    => frame_count > 1
    => uses_transparency IS NULL

NotPng
    => detected_format IS NOT NULL
    => detected_format <> 'PNG'
```

Keep exception text out of persisted user-visible fields.

Logs may retain detailed diagnostics.

---

# 16. Phased implementation

## Increment 1 — Contract, library and transport feasibility

Implement first:

* final vocabulary;
* recommendation semantics;
* ImageSharp dependency;
* binary image fixtures;
* APNG fixture proof;
* metadata-stripped normalized PNG/WebP comparison proof;
* analyzer/comparison profile constants;
* split 2 MB default / 8 MB absolute transport limit;
* safe-transport regression tests.

Do **not** build persistence or UI yet.

### Gate

The team can safely:

```text
fetch <=8 MB
identify PNG
identify APNG
decode bounded image
detect alpha
encode normalized PNG
encode lossless WebP
count output bytes
```

---

# Increment 2 — Shared site-analysis infrastructure

Create:

```text
Application/SiteAnalysis/
Infrastructure/SiteAnalysis/
```

Implement:

```text
ISiteAnalysisFetcher
SiteAnalysisFetchProfile
SiteAnalysisFetchRequest
SiteAnalysisFetchResult
SiteAnalysisRequestBudget
```

Then refactor:

```text
CrawlRequestExecutor
```

to delegate network execution to it.

Introduce:

```text
IHtmlDocumentDiscoveryExtractor
```

and preserve `IHtmlLinkExtractor` through an adapter.

### Required regression tests

All existing broken-link behavior must remain unchanged:

* redirects;
* robots;
* `<base>`;
* URL identity;
* retries;
* deduplication;
* truncation;
* request concurrency;
* external-link semantics;
* link classifications.

### Gate

Broken links behave identically before and after the refactor.

---

# Increment 3 — Bounded page and image discovery

Implement `PngSiteCrawler`.

Reuse for HTML pages:

```text
CrawlFrontier
CrawlScope
CrawlUrlNormalizer
robots handling
```

Implement separately:

```text
PngAssetScope
PngAssetUrlResolver
PngImageLedger
```

The asset scope uses allowed hosts without page path-prefix rules.

Implement:

* `img[src]`;
* `img[srcset]`;
* `picture source[srcset]`;
* descriptors such as `1x`, `2x`, `640w`;
* `<base href>`;
* redirected document URL;
* request identity;
* display redaction;
* discovery skips;
* reference limits;
* source-mapping limits;
* total page-byte limits.

Do not parse `srcset` with naïve `Split(',')`.

### Gate

Fixture websites deterministically produce:

```text
pages
unique image request identities
source mappings
discovery skips
coverage reasons
```

without downloading image bodies yet.

---

# Increment 4 — PNG analyzer

Implement:

```text
PngImageAnalyzer
CountingStream
```

Pipeline:

```text
encoded bytes
   |
detect actual format
   |
identify dimensions/bit depth/frame count
   |
resource preflight
   |
if 16-bit -> stop
   |
validate APNG structure and CRCs through IEND
   |
if animated -> stop
   |
decode first/static frame
   |
alpha scan
   |
if transparent -> stop
   |
encode normalized PNG
   |
encode lossless WebP
   |
apply both thresholds
   |
return recommendation
```

Do not keep candidate encodings in a `MemoryStream`.

Use a counting/discard stream because only encoded size is needed.

Tests include:

* opaque RGB PNG;
* opaque RGBA PNG;
* transparent indexed PNG;
* one alpha pixel;
* semitransparent shadow;
* APNG;
* truncated and corrupt APNG;
* 16-bit PNG;
* corrupt PNG;
* truncated PNG;
* extensionless PNG;
* WebP pretending to be `.png`;
* wrong content type;
* huge dimensions;
* pixel-limit image;
* WebP smaller;
* WebP larger;
* metadata-heavy PNG;
* cancellation.

### Gate

Every binary fixture has a stable expected result.

---

# Increment 5 — Persistence and durable lifecycle

Add:

```text
PngAuditRun
PngAuditImageResult
PngAuditImageSource
PngAuditDiscoverySkip
PngAuditCoverageReason
```

Add:

```text
IPngAuditResultSink
IPngAuditReader
IPngAuditReconciler
```

Implement:

* `Queued` lifecycle;
* lease-based claim;
* heartbeat;
* attempt limits;
* immutable run snapshot;
* result batching;
* pagination;
* constraints;
* indexes;
* active-run uniqueness;
* endpoint purge.

Mandatory repository work:

```text
ApplicationDbContext.cs
Persistence/Migrations/*
Persistence/CompiledModels/*
EndpointPurgeCascade.cs
```

Compiled model regeneration is mandatory, not optional.

### Gate

Database tests prove:

* duplicate active run blocked;
* duplicate image identity blocked;
* multiple source mappings allowed;
* all state invariants enforced;
* endpoint purge removes every PNG row;
* migrations and compiled model agree.

---

# Increment 6 — Queue and end-to-end execution

Create:

```text
IPngAuditRunner
PngAuditRunner

IPngAuditRunQueue
HangfirePngAuditRunQueue

PngAuditRunJob
PngAuditQueuedRunReader

PngAuditExecutionService
PngAuditReconciliationJob
```

Use queue:

```text
image-audits
```

with:

```text
WorkerCount = 1
AutomaticRetry(Attempts = 0)
```

Application-level reconciliation handles lost jobs through the persisted state/lease model.

Execution:

```text
Queued
   |
worker claim
   |
Running
   |
crawl page
   |
discover image refs
   |
dedupe
   |
fetch image
   |
analyze
   |
persist batch
   |
heartbeat
   |
repeat
   |
terminal state
```

Add to DI:

```text
PngAuditOptions
shared site-analysis fetcher
PNG request gate
global decode semaphore
analyzer
sink
reader
runner
queue
job
reconciler
```

PNG auditing must be able to run even if:

```text
Crawling:Scheduling:Enabled = false
```

because it shares crawl **infrastructure**, not the broken-link feature switch.

Update Hangfire-enable logic accordingly.

Add:

```csharp
app.UsePngAuditScheduling();
```

### Gate

Tests prove:

* queue failure retires run;
* duplicate job delivery is harmless;
* expired lease is recoverable;
* stale queued job is re-enqueued;
* PNG consumes at most one HTTP slot;
* normal monitoring keeps its reservation;
* broken-link requests still execute during PNG work;
* decoder concurrency never exceeds one.

---

# Increment 7 — Tools UI and delivery

Only now expose the feature.

## Sidebar

Add:

```text
Tools
  PNG image audit
```

to `ShellNavigation`.

Do not build a generic Tools landing page yet.

Do not build:

```text
Tool
ToolRun
ToolResult
```

The second or third tool can justify a generalized architecture later.

---

## Routes

Use explicit attribute routing:

```csharp
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
[Route("Tools/PngImages")]
public sealed class PngAuditsController : Controller
{
    [HttpGet("")]
    public Task<IActionResult> Index(...);

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets)]
    [HttpPost("Run")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RunNow(...);

    [HttpGet("Status")]
    public Task<IActionResult> Status(...);

    [HttpGet("Runs/{id:guid}")]
    public Task<IActionResult> Run(Guid id, ...);
}
```

This is required because the application currently otherwise follows conventional `{controller}/{action}/{id?}` routing; the originally proposed `/Tools/PngImages` routes would not appear automatically. 

---

## Authorization

Read:

```text
ReadRegistry
```

Execute:

```text
TestRegistryTargets
+
IEndpointTestGate
```

Therefore:

| Role              | Read | Run |
| ----------------- | ---: | --: |
| Administrator     |  Yes | Yes |
| Operations        |  Yes | Yes |
| Developer/Support |  Yes | Yes |
| Viewer            |  Yes |  No |

---

## AJAX lifecycle

Follow the PageAudit pattern:

```text
POST Run
    ->
202 Accepted

GET Status
    ->
202 while Queued/Running
200 when terminal
```

Reuse:

```text
poller.js
run-status.js
AJAX fragment replacement
```

Do not create a new client-side polling system.

---

# 17. Results UI

## Summary

Show:

```text
Pages inspected
Images discovered
Unique images
PNGs analyzed
Uses transparency
Opaque PNGs
WebP candidates
Below threshold
Skipped / not analyzed
```

Coverage should be shown separately:

```text
Site crawl coverage
Image-analysis coverage
Source-mapping coverage
```

---

## Results table

Columns:

```text
Image
Used on
Dimensions
Current size
Transparency
Recommendation
Lossless WebP size
Potential saving
```

Filters:

```text
All

WebP candidates
Uses transparency
Below WebP threshold
Animated PNG
Not analyzed
Not PNG
```

---

## Example wording

Good:

> **Opaque PNG — lossless WebP candidate**
> Current: 184 KB · WebP: 112 KB · estimated reduction: 72 KB / 39%

Good:

> **Uses transparency**
> This image is not eligible for the opaque-PNG recommendation used by this version of the tool.

Good:

> **Opaque PNG — lossless WebP below threshold**
> Conversion saved 2.1%, below the configured 10% / 4 KB recommendation threshold.

Bad:

> PNG should be changed.

Bad:

> Keep PNG.

Bad:

> Transparent background detected.

The tool detects **alpha usage**, not necessarily a transparent background.

---

# 18. Image previews

Do not use:

```html
<img src="https://target-site/...">
```

in the report.

That would cause the user's browser to bypass:

* WebHealth SSRF controls;
* rate limits;
* server-side transport policy;
* request logging;
* image limits.

V1 should display URLs only.

A future thumbnail feature may generate and serve bounded local thumbnails.

---

# 19. Existing files expected to change

At minimum:

```text
Directory.Packages.props
src/WebHealth.Infrastructure/WebHealth.Infrastructure.csproj
affected packages.lock.json files

src/WebHealth.Infrastructure/DependencyInjection.cs
src/WebHealth.Web/Program.cs
src/WebHealth.Web/appsettings.json

src/WebHealth.Application/Monitoring/ISafeHttpTransport.cs
src/WebHealth.Infrastructure/Monitoring/SafeHttpTransport.cs

src/WebHealth.Infrastructure/Crawling/CrawlRequestExecutor.cs
src/WebHealth.Infrastructure/Crawling/HtmlLinkExtractor.cs

src/WebHealth.Infrastructure/Persistence/ApplicationDbContext.cs
src/WebHealth.Infrastructure/Persistence/Migrations/*
src/WebHealth.Infrastructure/Persistence/CompiledModels/*

src/WebHealth.Infrastructure/Registry/EndpointPurgeCascade.cs

src/WebHealth.Web/Shell/ShellNavigation.cs
src/WebHealth.Web/Views/Shared/_Icon.cshtml

tests/WebHealth.IntegrationTests/Support/WebHealthWebApplicationFactory.cs
tests/WebHealth.IntegrationTests/Support/CrawlTestHarness.cs
tests/WebHealth.IntegrationTests/Support/DatabaseFoundationAssertions.cs
```

Plus the new `SiteAnalysis` and `PngAudits` files.

The audit correctly called these existing integration points out as missing from the original plan. 

---

# 20. Documentation

Do **not** use:

```text
docs/phase-8/
```

because Phase 8 already has another roadmap meaning. 

Use:

```text
docs/General/PNG_Image_Audit_Implementation_Plan.md
docs/General/PNG_Image_Audit_Operations_and_Decisions.md
```

Two documents are enough for this project.

---

# 21. Definition of done

V1 is finished only when:

1. PNG auditing uses a separate `PngAudits` subsystem.
2. No generic Tools framework was introduced prematurely.
3. Broken-link crawling still passes its complete regression suite.
4. Safe transport keeps its normal 2 MB default.
5. PNG requests may explicitly request up to the new 8 MB absolute cap.
6. PNG auditing can run independently of broken-link scheduling.
7. Page scope and asset-host scope are separate.
8. Asset path prefixes do not incorrectly exclude `/assets/...`.
9. Arbitrary external hosts are not fetched in V1.
10. Fetch, identity and display URLs have separate contracts.
11. Sensitive image query values are never persisted or logged.
12. `src`, `srcset`, and `picture/source` are supported.
13. Navigation and image-reference extraction have independent coverage state.
14. Source mappings are explicitly bounded.
15. Actual image format is determined from bytes.
16. APNG receives no static conversion recommendation.
17. Transparency is determined from decoded alpha values.
18. Opaque PNG recommendations require a real WebP encoding.
19. Metadata differences cannot alone trigger a WebP recommendation.
20. Both absolute and percentage saving thresholds must pass.
21. Image binaries and converted candidates are never persisted.
22. The encoder writes to a counting/discard stream.
23. Image decoding is dimension, pixel and memory bounded.
24. PNG networking consumes at most one shared site-analysis HTTP slot in V1.
25. PNG decode/encoding concurrency is globally bounded.
26. Runs begin as `Queued`, not `Running`.
27. Workers claim runs through leases.
28. Long-running jobs renew their lease.
29. Duplicate deliveries cannot duplicate execution.
30. Run configuration is immutable.
31. Coverage is separated into crawl, image and source-mapping coverage.
32. Discovery skips have a persistence model.
33. All result-state combinations have database invariants.
34. Endpoint purge removes sources, results, skips, coverage reasons and runs.
35. UI execution requires `TestRegistryTargets`.
36. UI reads follow registry visibility.
37. `/Tools/PngImages` uses explicit routes.
38. polling uses the existing AJAX pattern.
39. reports do not directly embed target-hosted images.
40. CI performs no live site requests.
41. migrations and compiled EF models are current.
42. package locks and dependency checks pass.
43. the final documentation lives under `docs/General`.
44. A 16-bit PNG cannot receive an 8-bit lossless WebP recommendation.

---

## Implementation order

The important part is to **not start with the sidebar or database migration**.

The correct dependency order is:

```text
1. Contract + image-library proof + transport ceiling
                     ↓
2. Shared bounded fetch/document infrastructure
                     ↓
3. Page + image discovery
                     ↓
4. PNG analysis and comparison
                     ↓
5. Persistence + Queued/Running lifecycle
                     ↓
6. Hangfire execution + reconciliation + isolation
                     ↓
7. Tools UI + polling + final delivery tests
```

That is the major improvement over the previous plan. Resource safety and shared crawler integration are now architectural prerequisites instead of things retrofitted after the feature has already been built.

**Verdict:** with these changes, I would consider the plan ready to implement. The uploaded audit's twelve mandatory corrections are addressed: transport ceiling, shared fetch infrastructure, separate asset scope, URL representations, external-origin policy, queued lifecycle, schema completeness, fair resource usage, attribute routing, narrower result wording, missing bounds, and documentation placement. 

[1]: https://docs.sixlabors.com/articles/imagesharp/imageformats.html?utm_source=chatgpt.com "Image Formats"
[2]: https://sixlabors.com/pricing/?utm_source=chatgpt.com "Six Labors : Six Labors"

# PNG Image Audit Operations and Decisions

Implementation plan: [`PNG_Image_Audit_Implementation_Plan.md`](PNG_Image_Audit_Implementation_Plan.md)

## Increment 1 baseline

PNG auditing remains disabled by default. Increment 1 adds the analysis contract, stable profile names, recommendation thresholds, ImageSharp feasibility evidence, resource-limit configuration and the safe-transport ceiling needed by later increments. It does not add crawling, persistence, jobs or UI.

The stable profiles are:

```text
analyzer_profile   = png-alpha-v1
comparison_profile = normalized-png-vs-lossless-webp-v1
```

The comparison requires the configured byte and percentage thresholds to pass against both the original PNG and a metadata-stripped normalized PNG. The configured values create one validated recommendation policy; direct policy construction rejects negative byte thresholds and percentages outside 0–100.

Fetch outcomes and byte-analysis outcomes have separate typed classifications. Analyzer results expose validated image facts and comparison metrics through factory methods so invalid state combinations cannot be constructed through the public contract.

## Increment 2 shared site analysis

`ISiteAnalysisFetcher` now owns standard site-analysis transport execution, bounded transient retries, `Retry-After`, the shared request budget and per-host rate limiting. `CrawlRequestExecutor` is a thin adapter that supplies the existing crawl profile, including its per-host request rate, and redirect policy. Future PNG runs can therefore supply their own snapshotted rate without inheriting crawler configuration. The shared profile remains capped at the normal 2 MB transport ceiling; the separate PNG image transport capability from Increment 1 remains the only path that can request up to 8 MB and will be connected to PNG crawling in a later increment.

`SiteAnalysisRequestBudget` replaces the crawler-specific budget without changing its capacity. All site-analysis consumers resolve the same singleton budget, preserving at least half of the global safe-transport capacity for monitoring.

The shared host limiter admits requests through per-host gates, advances pacing only after a non-cancelled waiter is admitted and retains at most 4096 host states with idle-state eviction.

`IHtmlDocumentDiscoveryExtractor` parses navigation links, the first base URL, `img[src]`, `img[srcset]` and `picture source[srcset]` in one bounded pass. It deliberately ignores `source[src]`, which is outside V1. Navigation and image-reference completeness are reported independently. The existing `IHtmlLinkExtractor` remains as an adapter over navigation discovery, so broken-link behavior does not consume image data.

## Increment 3 bounded discovery

`IPngSiteCrawler` now produces the in-memory discovery result required by the later analyzer and persistence increments. It sequentially fetches internal HTML pages through `ISiteAnalysisFetcher`, applies the snapshotted PNG request rate, timeout and retry policy, and rejects redirects outside the page scope. It does not read or honour `robots.txt`: PNG audits run against endpoints the operator registered and owns, so page traversal is bounded by the configured page scope and limits alone.

Page traversal and image asset scope are separate. Pages require both an allowed host and an allowed path prefix. Image references require only an allowed asset host, so a page under `/application/` may validly discover `/assets/image.png`. External asset hosts, `data:`, `blob:`, unsupported schemes, credentials, malformed URLs and overlong values are recorded as typed discovery skips and are not fetched.

Discovered image requests retain an in-memory fetch URL, a SHA-256 identity hash and a bounded redacted display URL. Query order and authored query encoding remain part of the request identity, fragments are removed, and sensitive query values appear only as `REDACTED` in display and skip values. Image bodies are not downloaded in this increment.

The crawler resolves image and navigation references against the redirected document URL and the document's first valid `<base href>`. It uses the shared `srcset` parser rather than comma splitting, keeps descriptors on source mappings, and deduplicates each image request independently from its source mappings.

Discovery is bounded by page count, depth, page bytes, total page bytes, per-page image references, unique image requests, source mappings, HTTP attempts and duration. Coverage reasons remain separated across crawl, image-analysis and source-mapping areas. Reaching one limit does not falsely report complete coverage in another area.

The HTTP-attempt budget counts every outbound exchange, including redirect hops and retries. The remaining run budget constrains redirect traversal before each transport call, so a single redirect chain cannot exceed it. The run duration is also a linked cancellation deadline for active requests, rate-limit waits and retry delays; deadline cancellation returns partial discovery with `DurationLimit`, while caller cancellation still propagates.

Safe transport exposes the exact normalized final request URL only in memory while retaining its query-free final destination for logs and display. Page identity uses the requested URL when no redirect occurred and the exact final request URL after redirects. Asset fetch URLs retain their resolved authored form, while identity hashes use the same canonical request representation that transport sends, preserving query order and values while consolidating equivalent host, port and escape spellings.

## Increment 4 bounded image analysis

`IPngImageAnalyzer` now detects the encoded format from bytes and applies an immutable analysis-limit snapshot before allocating a decoded image. PNG dimensions, bit depth and APNG frame metadata come from bounded signature, `IHDR` and `acTL` inspection. Oversized dimensions, pixel counts and decoded RGBA memory estimates are rejected before decode. Valid 16-bit PNGs receive `UnsupportedBitDepth` instead of being reduced to 8-bit for a falsely lossless comparison.

Before a multi-frame PNG receives `AnimatedPng`, every chunk through the terminal `IEND` must be structurally bounded and CRC-valid. The validator checks APNG frame counts, sequence numbers, frame bounds and frame data. APNG pixels are not decoded in V1, so their transparency count, percentage and boolean state remain unknown rather than being manufactured as zero or false.

Static PNG files decode one frame with metadata skipped and ImageSharp parallelism fixed at one. Every decoded pixel is inspected; any alpha value below 255 classifies the image as using transparency and stops comparison. The singleton analyzer also serializes complete decode and encode work, matching the configured V1 decode concurrency of one.

Opaque images are compared using these stable `normalized-png-vs-lossless-webp-v1` settings:

```text
normalized PNG: 8-bit RGB, adaptive filter, compression level 9, metadata skipped
lossless WebP:   lossless mode, quality effort 100, method level 6, metadata skipped
```

Both encoders write into a discard-only counting stream, so candidate bodies are never retained in memory. The configured recommendation thresholds directly apply the absolute and percentage savings against both the fetched original and the normalized PNG. This prevents metadata removal alone from creating a WebP recommendation without adding a second policy wrapper.

## Increment 5 persistence and durable lifecycle

PNG audit runs now persist in five normalized tables for runs, unique image results, source mappings, discovery skips and coverage reasons. Each queued run stores the complete page scope, asset scope, query policy, discovery limits, image-analysis limits, recommendation thresholds and stable analyzer profiles needed to reconstruct the work without consulting mutable runtime settings. Collection-valued scope settings use structured serialization so an embedded delimiter cannot create additional hosts or path prefixes during reconstruction.

The lifecycle follows `Queued -> Running -> terminal` transitions with a partial unique index that permits only one active run per endpoint. Claims atomically assign a lease, increment the bounded attempt count and preserve the first start time. Heartbeats and every result batch extend the lease only while it is current, and stale workers cannot append results or complete a run after ownership moves to another attempt. Reconciliation identifies stale queued and expired running work and permanently fails attempts that have exhausted their configured limit.

Image result batches are idempotent by request identity. A unique image may retain multiple distinct page and attribute source mappings, discovery skips are deduplicated with structural nullable keys, and accumulated coverage counts use replacement semantics on replay. Each batch refreshes row-derived partial summaries, while completion verifies persisted rows and snapshotted hard limits before publishing terminal totals. The database enforces legal transport, analysis, transparency, comparison and recommendation state combinations, bounded summaries, failure-code vocabulary, unknown APNG transparency and signed savings.

The persistence boundary applies the snapshotted sensitive-query policy to seed, image, final, source-page and skip display values, and sanitizes terminal diagnostics. The exact seed is represented durably only by its SHA-256 identity so execution can verify the current endpoint target without storing query secrets. Reads apply the existing endpoint visibility boundary and paginate image results, source mappings and discovery skips with deterministic ordering. Endpoint purge explicitly removes all five PNG table layers before deleting the endpoint. Apply `20260827092012_PngAuditPersistenceHardening` with the normal explicit migration process before running code from this revision; it backfills seed identities and installs the strengthened checks.

## Increment 6 queue and execution

PNG audits use the dedicated Hangfire `image-audits` queue with exactly one worker and no Hangfire retries. `PngAudits:Enabled` remains `false` by default, but setting it to `true` now activates the queue, worker and minute reconciliation job even when `Crawling:Scheduling:Enabled` is `false`. Application leases and the configured maximum attempt count remain the only retry authority.

Manual queueing reuses the endpoint test gate, snapshots the current target and every discovery and analysis policy, commits the queued run, then enqueues it. A queue handoff failure immediately retires the durable run with `WorkerUnavailable`. At claim time the worker rechecks that the endpoint is testable and that its canonical target and production classification still match the snapshot identity.

The worker heartbeats while page discovery, image fetching and analysis run. It records discovery skips and coverage before processing each unique image, restores persisted image progress after an expired lease, and derives final image totals from durable rows. Duplicate delivery is harmless because only a queued run or an expired lease can be claimed. Stale queued and expired running identifiers are re-enqueued by application reconciliation; exhausted attempts are failed permanently.

PNG image transport is the only 8 MB path. It now acquires a one-slot PNG child gate and the shared site-analysis request budget, while the parent budget remains capped at half of global safe-transport concurrency. The other half stays reserved for normal monitoring, and remaining site-analysis slots stay available to broken-link work. PNG page and image requests use the run's own snapshotted `RequestsPerSecondPerHost` value. The singleton analyzer continues to serialize decode and encode work, but each execution supplies its snapshotted resource limits and recommendation thresholds rather than mutable startup values.

Lease recovery restores cumulative page-discovery, page-byte and outbound-request progress before rediscovery. The run deadline remains anchored to the first claim time across later lease claims, while rediscovered pages remain idempotent work rather than consuming the page-count limit twice. Image fetches apply the run's snapshotted transient retry policy, including bounded `Retry-After` handling, and every retry and redirect hop consumes the same cumulative outbound-request budget.

## Increment 7 tools UI

The feature is exposed at `/Tools/PngImages` under a new **Tools** sidebar section. All four application personas can read runs through the existing registry-visibility boundary. Administrator, Operations and Developer/Support can queue a run only when the existing endpoint test gate permits it; Viewer cannot queue work. The manual POST is anti-forgery protected and returns `202 Accepted` to the existing AJAX run poller. Status reads return `202` while the durable run is queued or running and `200` after a terminal transition.

The run screen separates recorded result totals from site-crawl, image-analysis and source-mapping coverage. Image filters execute in the database before deterministic pagination. Each row shows the redacted image URL, first recorded source page and source count, dimensions, response size, alpha-use state, classification, measured lossless WebP size and original-file saving. It never embeds a target-hosted image or persists a new preview artifact.

`PngAudits:Enabled` is now `true` in the normal committed runtime settings because queue execution and its authorized UI are both delivered. `appsettings.Testing.json` and the fresh-machine setup keep it off unless background work is explicitly enabled, preserving deterministic tests and the setup script's non-mutating demo mode. No schema change is part of this increment; `20260827092012_PngAuditPersistenceHardening` remains the latest required PNG migration.

## ImageSharp dependency decision

Reviewed on 2026-08-26.

The project directly references `SixLabors.ImageSharp` 3.1.12. This is the latest maintained 3.x release available at the review date, supports the project's .NET target and is not reported as vulnerable by the NuGet package page.

ImageSharp 3.1.12 uses the Six Labors Split License 1.0. The current personal internship and portfolio use fits the project's present non-commercial profile. Recheck the license before changing the project to closed-source commercial use, transferring it to a business, or changing the dependency version. This record is an engineering review and not legal advice.

ImageSharp 4.x was not selected because direct dependencies require a valid Six Labors build-time license. A future 4.x upgrade must first decide the applicable license, configure the license through local or CI secrets, and keep `sixlabors.lic` and license values out of source control.

References:

- [ImageSharp 3.1.12 package and license](https://www.nuget.org/packages/SixLabors.ImageSharp/3.1.12)
- [Six Labors licensing and pricing](https://sixlabors.com/pricing/)
- [ImageSharp documentation](https://docs.sixlabors.com/articles/imagesharp/index.html)

## Binary feasibility evidence

The fixture suite proves that ImageSharp 3.1.12 can:

- detect PNG from encoded bytes;
- decode RGBA pixels and find actual alpha values;
- decode a multi-frame APNG and expose more than one frame;
- write a metadata-stripped normalized PNG;
- write a lossless WebP;
- report the actual byte counts of both comparison encodings;
- preserve decoded pixels through the lossless WebP encode.

The APNG fixture is the ImageSharp 3.1.12 upstream `tests/Images/Input/Png/animated/apng.png` test asset with SHA-256 `7C15E4670DA1826D1CC25555BD6CBE287ECC70327CD029A7613334A39A283021`. The other fixtures are locally generated PNGs with fixed decoded-pixel expectations.

ImageSharp 3.1.12 `Image.Identify` rejects the known-valid APNG fixture even though `Image.DetectFormat` and full decoding succeed. Increment 4 therefore uses bounded PNG chunk inspection for dimensions and animation metadata before full pixel decoding instead of relying on `Image.Identify`.

## Transport limits

Normal monitoring and crawling continue to default to 2 MB. `SafeHttpTransport` implements only `ISafeHttpTransport` and rejects requests above that standard ceiling. The PNG-specific `PngImageTransport` adapter uses an internal extended operation and may explicitly request up to the 8 MB absolute ceiling. Requests beyond the ceiling for either path are rejected before outbound execution.

The `PngAudits` configuration snapshots the V1 defaults from the implementation plan and validates them during application startup. It includes the PNG-specific host request rate, maximum attempts, lease duration, heartbeat interval, reconciliation delay and reconciliation batch size. `Enabled: true` is accepted because queue, worker, reconciliation and the authorized Tools UI are connected. Normal runtime settings enable the feature; testing and fresh-machine setup keep it disabled unless background work is explicitly requested.

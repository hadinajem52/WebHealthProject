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

`IPngSiteCrawler` now produces the in-memory discovery result required by the later analyzer and persistence increments. It sequentially fetches internal HTML pages through `ISiteAnalysisFetcher`, applies the snapshotted PNG request rate, timeout and retry policy, obeys robots rules, and rejects redirects outside the page scope.

Page traversal and image asset scope are separate. Pages require both an allowed host and an allowed path prefix. Image references require only an allowed asset host, so a page under `/application/` may validly discover `/assets/image.png`. External asset hosts, `data:`, `blob:`, unsupported schemes, credentials, malformed URLs and overlong values are recorded as typed discovery skips and are not fetched.

Discovered image requests retain an in-memory fetch URL, a SHA-256 identity hash and a bounded redacted display URL. Query order and authored query encoding remain part of the request identity, fragments are removed, and sensitive query values appear only as `REDACTED` in display and skip values. Image bodies are not downloaded in this increment.

The crawler resolves image and navigation references against the redirected document URL and the document's first valid `<base href>`. It uses the shared `srcset` parser rather than comma splitting, keeps descriptors on source mappings, and deduplicates each image request independently from its source mappings.

Discovery is bounded by page count, depth, page bytes, total page bytes, per-page image references, unique image requests, source mappings, HTTP attempts and duration. Coverage reasons remain separated across crawl, image-analysis and source-mapping areas. Reaching one limit does not falsely report complete coverage in another area.

The HTTP-attempt budget counts every outbound exchange, including redirect hops and retries. The remaining run budget constrains redirect traversal before each transport call, so a single redirect chain cannot exceed it. The run duration is also a linked cancellation deadline for active requests, robots lookups, rate-limit waits and retry delays; deadline cancellation returns partial discovery with `DurationLimit`, while caller cancellation still propagates.

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

The `PngAudits` configuration snapshots the V1 defaults from the implementation plan and validates them during application startup. Since Increment 1 has no execution path, `Enabled: true` is rejected at startup instead of being accepted as a silent no-op. A later execution increment must remove that rejection only when it also wires scheduling, queue and worker activation.

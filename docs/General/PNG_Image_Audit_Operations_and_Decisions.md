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

ImageSharp 3.1.12 `Image.Identify` rejects the known-valid APNG fixture even though `Image.DetectFormat` and full decoding succeed. Increment 4 must not rely on `Image.Identify` alone for APNG preflight. It must use bounded PNG chunk inspection for dimensions and animation metadata before full pixel decoding, or re-evaluate the dependency version and license decision.

## Transport limits

Normal monitoring and crawling continue to default to 2 MB. `ISafeHttpTransport` rejects requests above that standard ceiling. The PNG-specific `IPngImageTransport` path may explicitly request up to the 8 MB absolute ceiling. Requests beyond the ceiling for either path are rejected before outbound execution.

The `PngAudits` configuration snapshots the V1 defaults from the implementation plan and validates them during application startup. Since Increment 1 has no execution path, `Enabled: true` is rejected at startup instead of being accepted as a silent no-op. A later execution increment must remove that rejection only when it also wires scheduling, queue and worker activation.

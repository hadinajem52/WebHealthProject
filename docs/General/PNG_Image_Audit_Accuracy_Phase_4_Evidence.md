# PNG Image Audit Accuracy — Phase 4 Evidence

**Date:** 28 August 2026

**Gate:** passed

## Delivered behavior

- Sixteen-bit PNGs are decoded as `Rgba64` instead of being reduced to 8-bit.
- Their decoded-memory preflight uses eight bytes per pixel.
- Dimensions and transparency facts are retained for both 16-bit RGB and RGBA inputs.
- The result is `HighBitDepthPng` with recommendation `None` and no comparison metrics.
- The result table states `Not applicable — WebP stores 8-bit channels` instead of implying that
  verification failed.

## Gate fixtures

The focused analyzer coverage generates 16-bit PNG inputs with ImageSharp's PNG encoder:

| Fixture | Expected result |
| --- | --- |
| 16-bit RGB, opaque | 8 × 8, opaque, `HighBitDepthPng`, no comparison, recommendation `None` |
| 16-bit RGBA, alpha 65534 | 8 × 8, transparency used, minimum displayed alpha 254, `HighBitDepthPng`, no comparison, recommendation `None` |
| 16-bit RGB under a 300-byte decode budget | `DecodedMemoryExceeded`; 64 pixels require 512 decoded bytes |

The rendered run-page fixture also verifies all three user-facing facts: the image is identified as
16-bit, transparency is reported, and WebP comparison is explicitly not applicable.

## Verification

```text
dotnet build --configuration Release --no-restore
Build succeeded. 0 warnings, 0 errors.

dotnet test tests/WebHealth.UnitTests/WebHealth.UnitTests.csproj \
  --configuration Release --filter "FullyQualifiedName~Png" --no-build --no-restore
Passed: 32, Failed: 0.

dotnet test tests/WebHealth.IntegrationTests/WebHealth.IntegrationTests.csproj \
  --configuration Release --filter "FullyQualifiedName~Png" --no-build --no-restore
Passed: 110, Failed: 0.
```

Phase 4 changes no database schema or persistence contract, so it requires no migration or setup
change.

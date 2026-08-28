# PNG Image Audit Accuracy — Phase 2 Evidence

**Date:** 28 August 2026

## Selected engine

Phase 2 uses `Magick.NET-Q8-AnyCPU` 14.16.0 as the pinned libwebp-backed encoder. ImageSharp
remains the PNG fact-gathering decoder and independently verifies the generated WebP dimensions
and every RGBA byte.

The encoder profile is lossless WebP, quality 100, method 6, and exact transparent RGB. EXIF and
XMP are removed. ICC profiles are retained and compared byte-for-byte with the WebP `ICCP` chunk.
The produced RIFF container must contain `VP8L` before its byte length is accepted.

Phase 2 measures the verified WebP against the current file exactly as stored, as required by the
Phase 2 output contract. The symmetrical metadata policy applies to the WebP and optimized-PNG
comparison candidates once the optimized-PNG reference is introduced in Phase 3; it does not
redefine the current-file byte count.

The NuGet download is approximately 99 MB because it contains native runtimes for all supported
platforms. The application runtime contribution is approximately 23.9 MB on Windows x64 and
36.6 MB on Linux x64, including the managed assembly. This is accepted for the local/demo portfolio
scope.

## Size comparison

Google's official `cwebp` 1.6.0 Windows x64 binary was run with:

```text
-lossless -q 100 -m 6 -exact -metadata icc
```

| Fixture | Source PNG | Magick.NET | cwebp 1.6.0 |
| --- | ---: | ---: | ---: |
| `metadata-heavy.png` | 43,831 bytes | 32 bytes | 32 bytes |
| `one-alpha-pixel.png` | 107 bytes | 42 bytes | 42 bytes |
| `opaque-rgba.png` | 121 bytes | 32 bytes | 32 bytes |

The checked fixtures are byte-size identical to direct `cwebp`; Magick.NET does not leave
substantial bytes on the table for these cases.

## Fidelity and platform evidence

- Opaque and transparent fixtures produce a lossless `VP8L` payload.
- A generated fully transparent pixel with non-zero hidden RGB verifies byte-exactly.
- An Adobe RGB ICC-profiled PNG carries the same 560-byte profile into the WebP `ICCP` chunk.
- The `one-alpha-pixel.png` fixture produces a verified 42-byte WebP on Windows x64.
- A self-contained Linux x64 spike run under Ubuntu on WSL also produces a verified 42-byte WebP.
- Encoding is serialized and bounded by image dimensions, decoded pixels, memory, disk, output
  bytes, and a configurable per-image timeout. Candidate writes stop at the output-byte limit.

## Remaining corpus gate

The original 15-image Audi Capital regression corpus is not checked into the repository. Phase 2
cannot claim its complete corpus gate until those exact source files or their archived run payloads
are available. The checked-in and generated acceptance cases cover opaque RGBA, transparency,
hidden transparent RGB, metadata removal, ICC preservation, VP8L validation, and cross-platform
execution.

## Configuration

`PngAudits:ComparisonTimeoutSeconds` defaults to 60 and accepts values from 1 through 300. The
native dependency is restored through the centrally pinned NuGet package and requires no loose
binary or machine-level installation.

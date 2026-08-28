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

## Acceptance corpus

The production discovery path was rerun against `https://www.audicapital.com/` on 28 August 2026.
It reproduced the audited baseline exactly: 28 inspected pages, 124 unique image requests, and 15
PNG responses. Each response passed VP8L inspection, independent ImageSharp decoding, dimension
equality, byte-exact RGBA comparison including hidden transparent RGB, and colour-policy
verification.

| Fixture | PNG bytes | Verified WebP | Savings | SHA-256 |
| --- | ---: | ---: | ---: | --- |
| `pic-website-660` | 383,199 | 254,174 | 33.67% | `b196e780b6bcda7280f68eb664c893f8108581529bbcda5be8caffbfd582a90b` |
| `for-website-660` | 284,510 | 196,654 | 30.88% | `6dfed370effeed8d417e99ea0b9a99850920aacf7590d6b95e998ec006e895c8` |
| `audi-tadawul-logo` | 12,888 | 8,302 | 35.58% | `109d84d4ff28a07f87daea47932ee1e912496323cf57095fb432b1dacadc5130` |
| `default-170806` | 10,013 | 3,894 | 61.11% | `090c9e18cab6e7433ec9b9b69674f71b45fabde6863e1e48f7ab695289b6410` |
| `default-103814` | 10,013 | 3,894 | 61.11% | `090c9e18cab6e7433ec9b9b69674f71b45fabde6863e1e48f7ab695289b6410` |
| `celine-a4` | 1,511,412 | 1,004,500 | 33.54% | `c8b34264f6551670ea0f8c6bbfb3bf589ccced7664d677673ecde6a922736d8a` |
| `5-094533377` | 218,701 | 150,360 | 31.25% | `0288213a01494406c6a22cb7a027afbbae6544d8d3f949746da7ceedea1d9268` |
| `dragonfly-660` | 28,062 | 15,892 | 43.37% | `38d0f923be2bbf8604852f4005d727e906f13ce469c11b6080faf47bc46606b9` |
| `pic-website-1900` | 1,860,251 | 1,219,610 | 34.44% | `d4e6fb933b01860b633a5b39fb152adc3599295abd24864cb49540fa424063f2` |
| `pic-website-920` | 695,666 | 433,534 | 37.68% | `1212df12074438dd8fb7d5071cb806220aec179a27a07e24d3321f54acd15762` |
| `for-website-920` | 526,672 | 349,972 | 33.55% | `5f955e762c27661f69b5112bf1b177584e1e953007daa9f8f9a0d16ca4e4089e` |
| `5-150506221` | 218,701 | 150,360 | 31.25% | `0288213a01494406c6a22cb7a027afbbae6544d8d3f949746da7ceedea1d9268` |
| `12-151959833` | 192,357 | 130,910 | 31.94% | `236ae72ee1e30abd7c4b20ed551e235351e8946c734449a520708070320b7a94` |
| `screenshot-2026-02-19` | 3,359,832 | 1,796,254 | 46.54% | `3a3030c967d5a8fbf864e72be297461a2b6eef80dc99e5999b04b8f168d6bd59` |
| `dragonfly-920` | 40,663 | 22,694 | 44.19% | `d048c77535c648ad2315f67b92a0f94bf7d309a7cfa665b2e025c6c52c707a54` |
| **Total** | **9,352,940** | **5,741,004** | **38.62%** | |

All 15 results were `ComparisonUnavailable` with `OptimizedPngReferencePending` after producing the
verified WebP measurement. This is the required Phase 2 outcome: verified savings versus the
current file are available, while no format-change recommendation is emitted before the Phase 3
optimized-PNG reference exists.

## Configuration

`PngAudits:ComparisonTimeoutSeconds` defaults to 60 and accepts values from 1 through 300. The
native dependency is restored through the centrally pinned NuGet package and requires no loose
binary or machine-level installation.

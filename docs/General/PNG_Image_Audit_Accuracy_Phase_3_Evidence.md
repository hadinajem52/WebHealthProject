# PNG Image Audit Accuracy — Phase 3 Evidence

**Date:** 28 August 2026

## Pinned optimized-PNG reference

Phase 3 bundles the official oxipng 10.2.0 Windows x64 and Linux x64-musl executables. The engine
runs `-o max --strip safe --quiet --timeout 60 --out <output> <input>` against the original encoded
PNG. It does not pass `--alpha`. The optimizer runs only after the independently verified lossless
WebP candidate beats the original-file thresholds.

| Runtime | Executable SHA-256 |
| --- | --- |
| Windows x64 | `394FEF4CCBC6EE5A50BA96FE75AF3557C4365349EB80371EF9CCC76F903C2530` |
| Linux x64-musl | `A2C08BF8F9914A03FC8A1D74E17E2F34A6BAC3616126627A5DD824DA5D6529E7` |

The optimizer output is independently decoded with ImageSharp. Its dimensions and every RGBA byte,
including hidden RGB under fully transparent pixels, must match the original. Declared sRGB state
and ICC profiles must also match. EXIF and XMP are excluded symmetrically by WebP profile removal
and oxipng's safe stripping policy.

## Decision gate

The Phase 3 palette and 1-bit grayscale fixtures start as unoptimized PNGs. WebP materially beats
the current file, which admits the optimized-PNG step, but does not beat the verified optimized
reference by both configured thresholds. Both fixtures therefore produce
`OptimizedPngPreferred` with `OptimizePng`, not `LosslessWebp`.

An optimizer failure produces `ComparisonUnavailable`, retains the verified WebP measurement, and
emits no recommendation. A below-original-threshold WebP skips oxipng. Unit and integration tests
cover both full recommendations, the skip, and the partial-unavailable result.

## Acceptance corpus

The preserved Audi Capital corpus contains 15 responses represented by 13 unique byte hashes. All
are static 8-bit PNGs. Colour type 6 is RGBA and colour type 2 is RGB.

| SHA-256 | Dimensions | Type | Original | WebP | Optimized PNG | Classification |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `0288213a01494406c6a22cb7a027afbbae6544d8d3f949746da7ceedea1d9268` | 275×386 | 6 | 218,701 | 150,360 | 209,901 | `VerifiedWebpCandidate` |
| `090c9e18cab6e7433ec9b9b69674f71b45fabde6863e1e48f7ab695289b6410e` | 380×380 | 6 | 10,013 | 3,894 | 4,400 | `OptimizedPngPreferred` |
| `109d84d4ff28a07f87daea47932ee1e912496323cf57095fb432b1dacadc5130` | 205×72 | 6 | 12,888 | 8,302 | 9,863 | `OptimizedPngPreferred` |
| `1212df12074438dd8fb7d5071cb806220aec179a27a07e24d3321f54acd15762` | 920×550 | 2 | 695,666 | 433,534 | 612,957 | `VerifiedWebpCandidate` |
| `236ae72ee1e30abd7c4b20ed551e235351e8946c734449a520708070320b7a94` | 275×386 | 6 | 192,357 | 130,910 | 179,587 | `VerifiedWebpCandidate` |
| `38d0f923be2bbf8604852f4005d727e906f13ce469c11b6080faf47bc46606b9` | 660×400 | 2 | 28,062 | 15,892 | 22,351 | `VerifiedWebpCandidate` |
| `3a3030c967d5a8fbf864e72be297461a2b6eef80dc99e5999b04b8f168d6bd59` | 1825×1606 | 6 | 3,359,832 | 1,796,254 | 2,575,014 | `VerifiedWebpCandidate` |
| `5f955e762c27661f69b5112bf1b177584e1e953007daa9f8f9a0d16ca4e4089e` | 920×550 | 2 | 526,672 | 349,972 | 477,158 | `VerifiedWebpCandidate` |
| `6dfed370effeed8d417e99ea0b9a99850920aacf7590d6b95e998ec006e895c8` | 660×400 | 2 | 284,510 | 196,654 | 260,062 | `VerifiedWebpCandidate` |
| `b196e780b6bcda7280f68eb664c893f8108581529bbcda5be8caffbfd582a90b` | 660×400 | 2 | 383,199 | 254,174 | 349,417 | `VerifiedWebpCandidate` |
| `c8b34264f6551670ea0f8c6bbfb3bf589ccced7664d677673ecde6a922736d8a` | 1024×1024 | 2 | 1,511,412 | 1,004,500 | 1,328,921 | `VerifiedWebpCandidate` |
| `d048c77535c648ad2315f67b92a0f94bf7d309a7cfa665b2e025c6c52c707a54` | 920×550 | 2 | 40,663 | 22,694 | 34,285 | `VerifiedWebpCandidate` |
| `d4e6fb933b01860b633a5b39fb152adc3599295abd24864cb49540fa424063f2` | 1900×800 | 2 | 1,860,251 | 1,219,610 | 1,699,853 | `VerifiedWebpCandidate` |

The 15-response set repeats hashes `028821...` and `090c9e...` once each. Across all 15 responses:

| Measure | Value |
| --- | ---: |
| Original PNG bytes | 9,352,940 |
| Verified WebP bytes | 5,741,004 |
| Verified optimized-PNG reference bytes | 7,978,070 |
| Savings against optimized references | 2,237,066 bytes, 28.04% |
| `VerifiedWebpCandidate` / `LosslessWebp` | 12 |
| `OptimizedPngPreferred` / `OptimizePng` | 3 |
| `ComparisonUnavailable` | 0 |

The final 13-unique-file local pass took approximately 208 seconds. Individual files ranged from
1.06 to 34.99 seconds, below the 60-second per-image limit. Allowing oxipng's own parallel trials
removed timeouts while the application continues to serialize comparisons.

## Verification gates

- Release build: zero warnings and zero errors.
- Release solution build: zero warnings and zero errors.
- PNG unit tests: 32 passed.
- PNG integration tests: 108 passed.
- Database foundation suite: its single 22-stage ordered test passed.
- Entity Framework pending-model check: no changes since the latest migration.

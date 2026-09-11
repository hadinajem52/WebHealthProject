using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using WebHealth.Application.PngAudits;
using WebHealth.Infrastructure.PngAudits;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngImageAnalyzerTests
{
    [Theory]
    [InlineData(PngColorType.Rgb)]
    [InlineData(PngColorType.RgbWithAlpha)]
    public async Task AnalyzeAsync_ClassifiesOpaqueRgbAndRgbaPngs(PngColorType colorType)
    {
        var bytes = CreatePng(colorType, byte.MaxValue);

        var result = await CreateAnalyzer().AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.VerifiedWebpCandidate);
        result.Image.Should().NotBeNull();
        result.Image!.UsesTransparency.Should().BeFalse();
        result.Image.FrameCount.Should().Be(1);
        result.Comparison.Should().NotBeNull();
        result.Comparison!.OptimizedPngBytes.Should().BePositive();
        result.Comparison.CandidateWebpBytes.Should().BePositive();
    }

    [Fact]
    public async Task AnalyzeAsync_ComparesATransparentIndexedPngInsteadOfExcludingIt()
    {
        var bytes = CreatePng(PngColorType.Palette, 0);

        var result = await CreateAnalyzer().AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.VerifiedWebpCandidate);
        result.Image!.TransparentPixelCount.Should().Be(1);
        result.Image.UsesTransparency.Should().BeTrue();
        result.Comparison.Should().NotBeNull(
            "transparency must no longer terminate the comparison path");
        result.Comparison!.CandidateWebpBytes.Should().BePositive();
    }

    [Theory]
    [InlineData("one-alpha-pixel.png", 8, 8, 1)]
    [InlineData("opaque-rgba.png", 32, 24, 0)]
    public async Task AnalyzeAsync_UsesDecodedAlphaValues(
        string fixtureName,
        int expectedWidth,
        int expectedHeight,
        long expectedTransparentPixels)
    {
        var result = await CreateAnalyzer().AnalyzeAsync(LoadFixture(fixtureName));

        result.Image!.TransparentPixelCount.Should().Be(expectedTransparentPixels);
        result.Image.Width.Should().Be(expectedWidth);
        result.Image.Height.Should().Be(expectedHeight);
        result.Image.UsesTransparency.Should().Be(expectedTransparentPixels > 0);
        result.Classification.Should().Be(
            PngImageAnalysisClassification.VerifiedWebpCandidate);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsSemitransparentPixels()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(
            CreatePng(PngColorType.RgbWithAlpha, 128));

        result.Classification.Should().Be(PngImageAnalysisClassification.VerifiedWebpCandidate);
        result.Image!.TransparentPixelCount.Should().Be(1);
        result.Image.Transparency!.SemiTransparentPixelCount.Should().Be(1);
        result.Image.Transparency.FullyTransparentPixelCount.Should().Be(0);
        result.Image.Transparency.BackgroundTransparentPixelCount.Should().Be(0);
        result.Image.Transparency.MinAlpha.Should().Be(128);
        result.Image.Transparency.HasTransparentBackground(1.0m).Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_StopsAtApngPreflightWithoutAStaticRecommendation()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(LoadFixture("animated.png"));

        result.Classification.Should().Be(PngImageAnalysisClassification.AnimatedPng);
        result.Image!.Width.Should().Be(32);
        result.Image.Height.Should().Be(32);
        result.Image.FrameCount.Should().Be(5);
        result.Image.TransparentPixelCount.Should().BeNull();
        result.Image.TransparentPixelPercent.Should().BeNull();
        result.Image.UsesTransparency.Should().BeNull();
        result.Comparison.Should().BeNull();
        result.Recommendation.Should().Be(PngRecommendation.None);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsTruncatedAndCorruptApngMetadata()
    {
        var valid = LoadFixture("animated.png");
        var truncated = valid[..^12];
        var corrupt = CorruptChunkData(valid, "fdAT"u8);
        using var analyzer = CreateAnalyzer();

        var truncatedResult = await analyzer.AnalyzeAsync(truncated);
        var corruptResult = await analyzer.AnalyzeAsync(corrupt);

        truncatedResult.Classification.Should().Be(
            PngImageAnalysisClassification.IdentificationFailed);
        corruptResult.Classification.Should().Be(
            PngImageAnalysisClassification.IdentificationFailed);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsAnApngWithAMismatchedDeclaredFrameCount()
    {
        var invalid = WithDeclaredFrameCount(LoadFixture("animated.png"), 6);

        var result = await CreateAnalyzer().AnalyzeAsync(invalid);

        result.Classification.Should().Be(
            PngImageAnalysisClassification.IdentificationFailed);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsCorruptAndTruncatedPngs()
    {
        var valid = LoadFixture("opaque-rgba.png");
        var corrupt = CorruptImageData(valid);
        var truncated = valid[..20];
        var analyzer = CreateAnalyzer();

        var corruptResult = await analyzer.AnalyzeAsync(corrupt);
        var truncatedResult = await analyzer.AnalyzeAsync(truncated);

        corruptResult.Classification.Should().Be(PngImageAnalysisClassification.DecodeFailed);
        truncatedResult.Classification.Should().Be(
            PngImageAnalysisClassification.IdentificationFailed);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesTheEncodedFormatWithoutAFileNameOrContentType()
    {
        var pngResult = await CreateAnalyzer().AnalyzeAsync(LoadFixture("opaque-rgba.png"));
        var webpResult = await CreateAnalyzer().AnalyzeAsync(CreateLosslessWebp());

        pngResult.Classification.Should().Be(
            PngImageAnalysisClassification.VerifiedWebpCandidate);
        webpResult.Classification.Should().Be(PngImageAnalysisClassification.NotPng);
        webpResult.DetectedFormat.Should().Be("Webp");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\"></svg>")]
    [InlineData("  \n<svg width=\"8\" height=\"8\"/>")]
    [InlineData("<?xml version=\"1.0\"?><svg width=\"8\"></svg>")]
    [InlineData("<!-- a comment --><svg width=\"8\"></svg>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"svg11.dtd\"><svg/>")]
    public async Task AnalyzeAsync_ReportsSvgAsNotPngRatherThanUnidentified(string markup)
    {
        var result = await CreateAnalyzer().AnalyzeAsync(Encoding.UTF8.GetBytes(markup));

        result.Classification.Should().Be(PngImageAnalysisClassification.NotPng);
        result.DetectedFormat.Should().Be("SVG");
    }

    [Theory]
    [InlineData("<html><body>not an image</body></html>")]
    [InlineData("<svgfoo/>")]
    [InlineData("<!-- unterminated comment <svg/>")]
    [InlineData("not markup at all")]
    public async Task AnalyzeAsync_DoesNotMistakeOtherTextForSvg(string markup)
    {
        var result = await CreateAnalyzer().AnalyzeAsync(Encoding.UTF8.GetBytes(markup));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.IdentificationFailed);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsDimensionsBeforeDecoding()
    {
        var bytes = WithDimensions(LoadFixture("opaque-rgba.png"), 10001, 24);

        var result = await CreateAnalyzer().AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.DimensionsExceeded);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsPixelCountBeforeDecoding()
    {
        var bytes = WithDimensions(LoadFixture("opaque-rgba.png"), 100, 100);
        var limits = new PngImageAnalysisLimits(8 * 1024 * 1024, 1000, 1000, 5000, 40000);

        var result = await CreateAnalyzer(limits).AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.PixelLimitExceeded);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsDecodedMemoryBeforeDecoding()
    {
        var limits = new PngImageAnalysisLimits(8 * 1024 * 1024, 1000, 1000, 1000, 1024);

        var result = await CreateAnalyzer(limits).AnalyzeAsync(LoadFixture("opaque-rgba.png"));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.DecodedMemoryExceeded);
    }

    [Fact]
    public async Task AnalyzeAsync_TreatsAntiAliasedCornersAsNotATransparentBackground()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(CreateRoundedCornerPng(241));

        var transparency = result.Image!.Transparency!;
        transparency.UsesTransparency.Should().BeTrue();
        transparency.SemiTransparentPixelCount.Should().Be(4);
        transparency.FullyTransparentPixelCount.Should().Be(0);
        transparency.BackgroundTransparentPixelCount.Should().Be(0);
        transparency.MinAlpha.Should().Be(241);
        transparency.BackgroundCoveragePercent.Should().Be(0m);
        transparency.HasTransparentBackground(1.0m).Should().BeFalse(
            "anti-aliased corners contain no fully transparent pixels");
    }

    [Fact]
    public async Task AnalyzeAsync_SeparatesBorderConnectedBackgroundFromInteriorHoles()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(CreateCutOutLogoPng());

        var transparency = result.Image!.Transparency!;
        transparency.FullyTransparentPixelCount.Should().Be(
            transparency.BackgroundTransparentPixelCount
            + transparency.InteriorTransparentPixelCount);
        transparency.BackgroundTransparentPixelCount.Should().Be(64);
        transparency.InteriorTransparentPixelCount.Should().Be(
            4,
            "an enclosed hole is not part of the background");
        transparency.HasTransparentBackground(1.0m).Should().BeTrue();
        result.Comparison.Should().NotBeNull(
            "a transparent background must still reach the comparison path");
    }

    [Theory]
    [InlineData(PngColorType.Rgb, 65535, false, 255)]
    [InlineData(PngColorType.RgbWithAlpha, 65534, true, 254)]
    public async Task AnalyzeAsync_ReportsSixteenBitFactsWithoutAWebpRecommendation(
        PngColorType colorType,
        ushort alpha,
        bool usesTransparency,
        byte minAlpha)
    {
        using var analyzer = CreateAnalyzer(
            thresholds: new PngRecommendationThresholds(0, 0));

        var result = await analyzer.AnalyzeAsync(CreateSixteenBitPng(colorType, alpha));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.HighBitDepthPng);
        result.Image.Should().NotBeNull("16-bit facts are reported, not discarded");
        result.Image!.Width.Should().Be(8);
        result.Image.Height.Should().Be(8);
        result.Image!.BitDepth.Should().Be(16);
        result.Image.Transparency.Should().NotBeNull();
        result.Image.Transparency!.UsesTransparency.Should().Be(usesTransparency);
        result.Image.Transparency.MinAlpha.Should().Be(minAlpha);
        result.Comparison.Should().BeNull(
            "lossless WebP stores 8-bit channels, so no exact equivalent exists");
        result.Recommendation.Should().Be(PngRecommendation.None);
    }

    [Fact]
    public async Task AnalyzeAsync_BudgetsEightBytesPerPixelForSixteenBitPngs()
    {
        var limits = new PngImageAnalysisLimits(8 * 1024 * 1024, 1000, 1000, 1000, 300);

        var result = await CreateAnalyzer(limits).AnalyzeAsync(
            CreateSixteenBitPng(PngColorType.Rgb, ushort.MaxValue));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.DecodedMemoryExceeded);
    }

    [Fact]
    public async Task AnalyzeAsync_VerifiesAnIccProfiledPng()
    {
        var result = await CreateAnalyzer(
                thresholds: new PngRecommendationThresholds(0, 0))
            .AnalyzeAsync(PngFixtureFactory.CreateIccProfilePng());

        result.Comparison.Should().NotBeNull(result.UnavailableReason);
        result.Comparison!.CandidateWebpBytes.Should().BePositive();
        result.Comparison.OptimizedPngBytes.Should().BePositive();
        result.Recommendation.Should().Be(PngRecommendation.OptimizePng);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsAnEncodedBodyAboveItsLimit()
    {
        var limits = new PngImageAnalysisLimits(100, 1000, 1000, 1000, 4000);

        var result = await CreateAnalyzer(limits).AnalyzeAsync(LoadFixture("opaque-rgba.png"));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.IdentificationFailed);
    }

    [Fact]
    public async Task AnalyzeAsync_VerifiesThePinnedOptimizedPngReference()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(LoadFixture("metadata-heavy.png"));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.VerifiedWebpCandidate);
        result.Comparison!.OriginalPngBytes.Should().Be(43831);
        result.Comparison.OptimizedPngBytes.Should().BePositive();
        result.Recommendation.Should().Be(PngRecommendation.LosslessWebp);
    }

    [Fact]
    public async Task AnalyzeAsync_RecommendsWebpOnlyAfterVerifyingTheOptimizedReference()
    {
        var bytes = CreatePng(
            PngColorType.Rgb,
            byte.MaxValue,
            PngCompressionLevel.NoCompression,
            256);
        var thresholds = new PngRecommendationThresholds(10, 1);

        var result = await CreateAnalyzer(thresholds: thresholds).AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.VerifiedWebpCandidate);
        result.Comparison!.CandidateWebpBytes.Should().BeLessThan(result.Comparison.OriginalPngBytes);
        result.Comparison.OptimizedPngBytes.Should().BePositive();
        result.Recommendation.Should().Be(
            PngRecommendation.LosslessWebp,
            "the candidate beats the verified optimized-PNG reference");
    }

    [Fact]
    public async Task AnalyzeAsync_UsesTheRunSnapshotInsteadOfLiveAnalyzerPolicy()
    {
        var bytes = CreatePng(
            PngColorType.Rgb,
            byte.MaxValue,
            PngCompressionLevel.NoCompression,
            256);
        using var analyzer = CreateAnalyzer(
            new PngImageAnalysisLimits(100, 10, 10, 100, 400),
            new PngRecommendationThresholds(100, long.MaxValue));

        var result = await analyzer.AnalyzeAsync(
            bytes,
            new PngImageAnalysisLimits(
                8 * 1024 * 1024,
                10000,
                10000,
                40000000,
                256L * 1024 * 1024),
            new PngRecommendationThresholds(10, 1));

        result.Classification.Should().Be(PngImageAnalysisClassification.VerifiedWebpCandidate);
        result.Comparison.Should().NotBeNull(
            "the per-call limits must override the analyzer's own limits");
        result.Recommendation.Should().Be(PngRecommendation.LosslessWebp);
    }

    [Fact]
    public async Task AnalyzeAsync_WithholdsAWebpCandidateBelowTheConfiguredThreshold()
    {
        var result = await CreateAnalyzer(
            thresholds: new PngRecommendationThresholds(90, 0))
            .AnalyzeAsync(CreateGradientPng(8));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.BelowWebpThreshold);
        result.Comparison!.CandidateWebpBytes.Should().BePositive();
        result.Comparison.OptimizedPngBytes.Should().BeNull();
        result.Recommendation.Should().Be(PngRecommendation.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnalyzeAsync_PrefersOptimizedPngWhenWebpDoesNotBeatTheReferenceThreshold(
        bool palette)
    {
        var bytes = palette
            ? PngFixtureFactory.CreatePaletteReferencePng(256)
            : PngFixtureFactory.CreateGrayscaleReferencePng(256);
        using var analyzer = CreateAnalyzer(
            thresholds: new PngRecommendationThresholds(10, 4096));

        var result = await analyzer.AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.OptimizedPngPreferred);
        result.Comparison!.OptimizedPngBytes.Should().BeLessThan(result.OriginalBytes);
        result.Recommendation.Should().Be(PngRecommendation.OptimizePng);
    }

    [Fact]
    public async Task AnalyzeAsync_WithholdsARecommendationWhenTheOptimizerFails()
    {
        using var analyzer = new PngImageAnalyzer(
            new PngImageAnalysisLimits(
                8 * 1024 * 1024,
                10000,
                10000,
                40000000,
                256L * 1024 * 1024),
            new PngRecommendationThresholds(0, 0),
            new StaticComparisonEngine(PngFormatComparisonResult.PartialUnavailable(
                50,
                "OptimizerFailed",
                PngAnalysisProfiles.VerifiedComparison)));

        var result = await analyzer.AnalyzeAsync(LoadFixture("opaque-rgba.png"));

        result.Classification.Should().Be(PngImageAnalysisClassification.ComparisonUnavailable);
        result.Comparison!.CandidateWebpBytes.Should().Be(50);
        result.Comparison.OptimizedPngBytes.Should().BeNull();
        result.Recommendation.Should().Be(PngRecommendation.None);
        result.UnavailableReason.Should().Be("OptimizerFailed");
    }

    [Fact]
    public async Task AnalyzeAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => CreateAnalyzer().AnalyzeAsync(
            LoadFixture("opaque-rgba.png"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnalyzeAsync_AbstainsWhenExifDeclaresANonIdentityOrientation()
    {
        var rotated = WithChunk(CreatePng(PngColorType.Rgb, byte.MaxValue), "eXIf"u8, ExifOrientation(6));

        var result = await CreateAnalyzer().AnalyzeAsync(rotated);

        result.Classification.Should().Be(PngImageAnalysisClassification.ComparisonUnavailable);
        result.Recommendation.Should().Be(PngRecommendation.None);
        result.UnavailableReason.Should().Be("OrientationNotPreserved");
        result.Image.Should().NotBeNull(
            "measured transparency and dimensions survive an abstention");
    }

    [Fact]
    public async Task AnalyzeAsync_ComparesWhenExifDeclaresTheIdentityOrientation()
    {
        var upright = WithChunk(CreatePng(PngColorType.Rgb, byte.MaxValue), "eXIf"u8, ExifOrientation(1));

        var result = await CreateAnalyzer().AnalyzeAsync(upright);

        result.Classification.Should().Be(PngImageAnalysisClassification.VerifiedWebpCandidate);
    }

    [Fact]
    public async Task AnalyzeAsync_KeepsCicpPrecedenceOverFallbackColorChunksThatFollowIt()
    {
        var source = CreatePng(PngColorType.Rgb, byte.MaxValue);
        var cicpThenSrgb = WithChunk(
            WithChunk(source, "cICP"u8, [1, 13, 0, 1]),
            "sRGB"u8,
            [0]);

        var result = await CreateAnalyzer().AnalyzeAsync(cicpThenSrgb);

        result.Classification.Should().Be(
            PngImageAnalysisClassification.ColorProfileUnsupported,
            "cICP outranks sRGB regardless of the order the chunks appear in");
        result.Recommendation.Should().Be(PngRecommendation.None);
    }

    [Theory]
    [InlineData(2247, PngImageAnalysisClassification.DecodedMemoryExceeded)]
    [InlineData(2248, PngImageAnalysisClassification.VerifiedWebpCandidate)]
    public async Task AnalyzeAsync_BudgetsBothDecodedCopiesAndBothEncodedCopies(
        long maxDecodedMemoryBytes,
        PngImageAnalysisClassification expected)
    {
        var source = CreatePng(PngColorType.Rgb, byte.MaxValue);
        var limits = new PngImageAnalysisLimits(
            100,
            10000,
            10000,
            40000000,
            maxDecodedMemoryBytes);

        var result = await CreateAnalyzer(limits).AnalyzeAsync(source);

        result.Classification.Should().Be(
            expected,
            "a 16x16 image needs 2048 bytes of decoded copies on top of the 200 bytes "
            + "reserved for the encoded source and candidate");
    }

    [Fact]
    public async Task AnalyzeAsync_ClassifiesAOneFrameApngAsAnimatedRatherThanUnidentifiable()
    {
        var singleFrame = SingleFrameApng();

        var result = await CreateAnalyzer().AnalyzeAsync(singleFrame);

        result.Classification.Should().Be(PngImageAnalysisClassification.AnimatedPng);
        result.Image!.FrameCount.Should().Be(1);
        result.Recommendation.Should().Be(PngRecommendation.None);
    }

    private static PngImageAnalyzer CreateAnalyzer(
        PngImageAnalysisLimits? limits = null,
        PngRecommendationThresholds? thresholds = null) =>
        new(
            limits ?? new PngImageAnalysisLimits(
                8 * 1024 * 1024,
                10000,
                10000,
                40000000,
                256L * 1024 * 1024),
            thresholds ?? new PngRecommendationThresholds(10, 1),
            new MagickPngFormatComparisonEngine(new PngAuditOptions()));

    private static byte[] LoadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PngAudits", name));

    private static byte[] CreatePng(
        PngColorType colorType,
        byte alpha,
        PngCompressionLevel compressionLevel = PngCompressionLevel.DefaultCompression,
        int size = 16)
    {
        using var image = new Image<Rgba32>(size, size, new Rgba32(32, 96, 160));
        image[size / 2, size / 2] = new Rgba32(200, 100, 50, alpha);
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            ColorType = colorType,
            CompressionLevel = compressionLevel,
            SkipMetadata = true
        });
        return output.ToArray();
    }

    private static byte[] CreateLosslessWebp()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3));
        using var output = new MemoryStream();
        image.SaveAsWebp(output, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 100,
            SkipMetadata = true
        });
        return output.ToArray();
    }

    private static byte[] CreateRoundedCornerPng(byte cornerAlpha)
    {
        using var image = new Image<Rgba32>(10, 10, new Rgba32(20, 40, 60, byte.MaxValue));
        image[0, 0] = new Rgba32(20, 40, 60, cornerAlpha);
        image[9, 0] = new Rgba32(20, 40, 60, cornerAlpha);
        image[0, 9] = new Rgba32(20, 40, 60, cornerAlpha);
        image[9, 9] = new Rgba32(20, 40, 60, cornerAlpha);
        return EncodePng(image);
    }

    private static byte[] CreateCutOutLogoPng()
    {
        using var image = new Image<Rgba32>(10, 10, new Rgba32(0, 0, 0, 0));
        for (var y = 2; y < 8; y++)
        {
            for (var x = 2; x < 8; x++)
            {
                image[x, y] = new Rgba32(200, 120, 40, byte.MaxValue);
            }
        }
        for (var y = 4; y < 6; y++)
        {
            for (var x = 4; x < 6; x++)
            {
                image[x, y] = new Rgba32(0, 0, 0, 0);
            }
        }
        return EncodePng(image);
    }

    private static byte[] EncodePng(Image<Rgba32> image)
    {
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            BitDepth = PngBitDepth.Bit8,
            ColorType = PngColorType.RgbWithAlpha,
            CompressionLevel = PngCompressionLevel.BestCompression,
            SkipMetadata = true
        });
        return output.ToArray();
    }

    private static byte[] CreateSixteenBitPng(PngColorType colorType, ushort alpha)
    {
        using var image = new Image<Rgba64>(
            8,
            8,
            new Rgba64(1000, 2000, 3000, alpha));
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            BitDepth = PngBitDepth.Bit16,
            ColorType = colorType,
            CompressionLevel = PngCompressionLevel.BestCompression,
            SkipMetadata = true
        });
        return output.ToArray();
    }

    private static byte[] CreateGradientPng(int size)
    {
        using var image = new Image<Rgba32>(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                image[x, y] = new Rgba32(
                    (byte)(x * 17),
                    (byte)(y * 29),
                    (byte)((x + y) * 11));
            }
        }

        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            ColorType = PngColorType.Rgb,
            CompressionLevel = PngCompressionLevel.BestCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            SkipMetadata = true
        });
        return output.ToArray();
    }

    private static byte[] CorruptImageData(byte[] source)
    {
        var result = source.ToArray();
        var chunkTypeOffset = result.AsSpan().IndexOf("IDAT"u8);
        var dataLength = BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(chunkTypeOffset - 4));
        result.AsSpan(chunkTypeOffset + 4, (int)dataLength).Clear();
        return result;
    }

    private static byte[] CorruptChunkData(byte[] source, ReadOnlySpan<byte> chunkType)
    {
        var result = source.ToArray();
        var chunkTypeOffset = result.AsSpan().IndexOf(chunkType);
        result[chunkTypeOffset + 8] ^= byte.MaxValue;
        return result;
    }

    private static byte[] WithDeclaredFrameCount(byte[] source, int frameCount)
    {
        var result = source.ToArray();
        var chunkTypeOffset = result.AsSpan().IndexOf("acTL"u8);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(chunkTypeOffset + 4), frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(chunkTypeOffset + 12),
            CalculatePngChecksum(result.AsSpan(chunkTypeOffset, 12)));
        return result;
    }

    private static byte[] WithDimensions(byte[] source, int width, int height)
    {
        var result = source.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(29),
            CalculatePngChecksum(result.AsSpan(12, 17)));
        return result;
    }

    private static byte[] ExifOrientation(ushort orientation)
    {
        var data = new byte[26];
        data[0] = 0x49;
        data[1] = 0x49;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), orientation);
        return data;
    }

    private static byte[] SingleFrameApng()
    {
        var source = CreatePng(PngColorType.Rgb, byte.MaxValue);
        var width = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(20));
        var animationControl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(animationControl, 1);
        var frameControl = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(frameControl.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt32BigEndian(frameControl.AsSpan(4), width);
        BinaryPrimitives.WriteUInt32BigEndian(frameControl.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16BigEndian(frameControl.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16BigEndian(frameControl.AsSpan(22), 10);
        return WithChunk(WithChunk(source, "acTL"u8, animationControl), "fcTL"u8, frameControl);
    }

    private static byte[] WithChunk(byte[] source, ReadOnlySpan<byte> chunkType, byte[] data)
    {
        var insertAt = source.AsSpan().IndexOf("IDAT"u8) - 4;
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        chunkType.CopyTo(chunk.AsSpan(4));
        data.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(
            chunk.AsSpan(8 + data.Length),
            CalculatePngChecksum(chunk.AsSpan(4, 4 + data.Length)));
        var result = new byte[source.Length + chunk.Length];
        source.AsSpan(0, insertAt).CopyTo(result);
        chunk.CopyTo(result.AsSpan(insertAt));
        source.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + chunk.Length));
        return result;
    }

    private static uint CalculatePngChecksum(ReadOnlySpan<byte> values)
    {
        var checksum = uint.MaxValue;
        foreach (var value in values)
        {
            checksum ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                checksum = (checksum & 1) == 0
                    ? checksum >> 1
                    : 0xEDB88320 ^ (checksum >> 1);
            }
        }

        return ~checksum;
    }

    private sealed class StaticComparisonEngine(PngFormatComparisonResult result)
        : IPngFormatComparisonEngine
    {
        public Task<PngFormatComparisonResult> CompareAsync(
            ReadOnlyMemory<byte> sourcePng,
            PngSourceEncodingFacts sourceFacts,
            PngRecommendationThresholds thresholds,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}

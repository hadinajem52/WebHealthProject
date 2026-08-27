using System.Buffers.Binary;
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

        result.Classification.Should().Be(PngImageAnalysisClassification.OpaqueBelowWebpThreshold);
        result.Image.Should().NotBeNull();
        result.Image!.UsesTransparency.Should().BeFalse();
        result.Image.FrameCount.Should().Be(1);
        result.Comparison.Should().NotBeNull();
        result.Comparison!.NormalizedPngBytes.Should().BePositive();
        result.Comparison.LosslessWebpBytes.Should().BePositive();
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsTransparencyInAnIndexedPng()
    {
        var bytes = CreatePng(PngColorType.Palette, 0);

        var result = await CreateAnalyzer().AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.UsesTransparency);
        result.Image!.TransparentPixelCount.Should().Be(1);
        result.Comparison.Should().BeNull();
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
        result.Classification.Should().Be(expectedTransparentPixels > 0
            ? PngImageAnalysisClassification.UsesTransparency
            : PngImageAnalysisClassification.OpaqueBelowWebpThreshold);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsSemitransparentPixels()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(
            CreatePng(PngColorType.RgbWithAlpha, 128));

        result.Classification.Should().Be(PngImageAnalysisClassification.UsesTransparency);
        result.Image!.TransparentPixelCount.Should().Be(1);
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
            PngImageAnalysisClassification.OpaqueBelowWebpThreshold);
        webpResult.Classification.Should().Be(PngImageAnalysisClassification.NotPng);
        webpResult.DetectedFormat.Should().Be("Webp");
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
    public async Task AnalyzeAsync_RejectsSixteenBitPngBeforeLosslessComparison()
    {
        using var analyzer = CreateAnalyzer(
            thresholds: new PngRecommendationThresholds(0, 0));

        var result = await analyzer.AnalyzeAsync(CreateSixteenBitPng());

        result.Classification.Should().Be(
            PngImageAnalysisClassification.UnsupportedBitDepth);
        result.Image.Should().BeNull();
        result.Comparison.Should().BeNull();
        result.Recommendation.Should().Be(PngRecommendation.None);
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
    public async Task AnalyzeAsync_RequiresSavingsAgainstTheNormalizedPng()
    {
        var result = await CreateAnalyzer().AnalyzeAsync(LoadFixture("metadata-heavy.png"));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.OpaqueBelowWebpThreshold);
        result.Comparison!.OriginalBytes.Should().Be(43831);
        result.Comparison.NormalizedPngBytes.Should().BeLessThan(result.Comparison.OriginalBytes);
        result.Recommendation.Should().Be(PngRecommendation.None);
    }

    [Fact]
    public async Task AnalyzeAsync_RecommendsARealSmallerLosslessWebp()
    {
        var bytes = CreatePng(
            PngColorType.Rgb,
            byte.MaxValue,
            PngCompressionLevel.NoCompression,
            256);
        var thresholds = new PngRecommendationThresholds(10, 1);

        var result = await CreateAnalyzer(thresholds: thresholds).AnalyzeAsync(bytes);

        result.Classification.Should().Be(PngImageAnalysisClassification.OpaqueWebpCandidate);
        result.Comparison!.LosslessWebpBytes.Should().BeLessThan(result.Comparison.OriginalBytes);
        result.Comparison.LosslessWebpBytes.Should().BeLessThan(
            result.Comparison.NormalizedPngBytes);
        result.Recommendation.Should().Be(PngRecommendation.LosslessWebp);
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

        result.Classification.Should().Be(PngImageAnalysisClassification.OpaqueWebpCandidate);
        result.Recommendation.Should().Be(PngRecommendation.LosslessWebp);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotRecommendALargerLosslessWebp()
    {
        var result = await CreateAnalyzer(
            thresholds: new PngRecommendationThresholds(0, 0))
            .AnalyzeAsync(CreateGradientPng(8));

        result.Classification.Should().Be(
            PngImageAnalysisClassification.OpaqueBelowWebpThreshold);
        result.Comparison!.LosslessWebpBytes.Should().BeGreaterThan(
            result.Comparison.OriginalBytes);
        result.Comparison.LosslessWebpBytes.Should().BeGreaterThan(
            result.Comparison.NormalizedPngBytes);
        result.Recommendation.Should().Be(PngRecommendation.None);
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
            thresholds ?? new PngRecommendationThresholds(10, 4096));

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

    private static byte[] CreateSixteenBitPng()
    {
        using var image = new Image<Rgba64>(
            8,
            8,
            new Rgba64(1000, 2000, 3000, ushort.MaxValue));
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            BitDepth = PngBitDepth.Bit16,
            ColorType = PngColorType.RgbWithAlpha,
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
}

namespace WebHealth.Application.PngAudits;

public interface IPngImageAnalyzer
{
    Task<PngAnalysisResult> AnalyzeAsync(
        ReadOnlyMemory<byte> encodedImage,
        CancellationToken cancellationToken = default);

    Task<PngAnalysisResult> AnalyzeAsync(
        ReadOnlyMemory<byte> encodedImage,
        PngImageAnalysisLimits limits,
        PngRecommendationThresholds recommendationThresholds,
        CancellationToken cancellationToken = default);
}

public enum PngImageAnalysisClassification
{
    NotPng,
    IdentificationFailed,
    DimensionsExceeded,
    PixelLimitExceeded,
    DecodedMemoryExceeded,
    AnimatedPng,
    DecodeFailed,
    UsesTransparency,
    WebpComparisonFailed,
    OpaqueWebpCandidate,
    OpaqueBelowWebpThreshold,
    UnsupportedBitDepth
}

public enum PngRecommendation
{
    None,
    LosslessWebp
}

public sealed record PngImageFacts
{
    public PngImageFacts(
        int width,
        int height,
        int frameCount,
        long pixelCount,
        long? transparentPixelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelCount);
        if (pixelCount != checked((long)width * height))
        {
            throw new ArgumentException("The pixel count must match the image dimensions.", nameof(pixelCount));
        }
        if (transparentPixelCount is < 0 || transparentPixelCount > pixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(transparentPixelCount));
        }

        Width = width;
        Height = height;
        FrameCount = frameCount;
        PixelCount = pixelCount;
        TransparentPixelCount = transparentPixelCount;
    }

    public int Width { get; }

    public int Height { get; }

    public int FrameCount { get; }

    public long PixelCount { get; }

    public bool? UsesTransparency => TransparentPixelCount is null
        ? null
        : TransparentPixelCount > 0;

    public long? TransparentPixelCount { get; }

    public decimal? TransparentPixelPercent => TransparentPixelCount is null
        ? null
        : TransparentPixelCount * 100m / PixelCount;
}

public sealed record PngComparisonMetrics
{
    public PngComparisonMetrics(
        long originalBytes,
        long normalizedPngBytes,
        long losslessWebpBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(originalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedPngBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(losslessWebpBytes);
        OriginalBytes = originalBytes;
        NormalizedPngBytes = normalizedPngBytes;
        LosslessWebpBytes = losslessWebpBytes;
    }

    public long OriginalBytes { get; }

    public long NormalizedPngBytes { get; }

    public long LosslessWebpBytes { get; }

    public long OriginalSavingsBytes => OriginalBytes - LosslessWebpBytes;

    public decimal OriginalSavingsPercent => OriginalSavingsBytes * 100m / OriginalBytes;

    public long NormalizedSavingsBytes => NormalizedPngBytes - LosslessWebpBytes;

    public decimal NormalizedSavingsPercent => NormalizedSavingsBytes * 100m / NormalizedPngBytes;
}

public sealed record PngAnalysisResult
{
    private PngAnalysisResult(
        PngImageAnalysisClassification classification,
        long originalBytes,
        string? detectedFormat = null,
        PngImageFacts? image = null,
        PngComparisonMetrics? comparison = null,
        PngRecommendation recommendation = PngRecommendation.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalBytes);
        Classification = classification;
        OriginalBytes = originalBytes;
        DetectedFormat = detectedFormat;
        Image = image;
        Comparison = comparison;
        Recommendation = recommendation;
    }

    public PngImageAnalysisClassification Classification { get; }

    public long OriginalBytes { get; }

    public string? DetectedFormat { get; }

    public PngImageFacts? Image { get; }

    public PngComparisonMetrics? Comparison { get; }

    public PngRecommendation Recommendation { get; }

    public static PngAnalysisResult NotPng(long originalBytes, string detectedFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedFormat);
        var normalizedFormat = detectedFormat.Trim();
        if (string.Equals(normalizedFormat, "PNG", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The detected format must not be PNG.", nameof(detectedFormat));
        }

        return new(PngImageAnalysisClassification.NotPng, originalBytes, normalizedFormat);
    }

    public static PngAnalysisResult Failed(
        PngImageAnalysisClassification classification,
        long originalBytes)
    {
        if (!IsFailureClassification(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        return new(classification, originalBytes);
    }

    public static PngAnalysisResult Animated(long originalBytes, PngImageFacts image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.FrameCount <= 1 || image.TransparentPixelCount is not null)
        {
            throw new ArgumentException(
                "An animated PNG must have multiple frames and unknown transparency.",
                nameof(image));
        }

        return new(PngImageAnalysisClassification.AnimatedPng, originalBytes, image: image);
    }

    public static PngAnalysisResult Transparent(long originalBytes, PngImageFacts image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.FrameCount != 1 || image.UsesTransparency is not true)
        {
            throw new ArgumentException("The image must be static and use transparency.", nameof(image));
        }

        return new(PngImageAnalysisClassification.UsesTransparency, originalBytes, image: image);
    }

    public static PngAnalysisResult ComparisonFailed(long originalBytes, PngImageFacts image)
    {
        EnsureStaticOpaque(image);
        return new(PngImageAnalysisClassification.WebpComparisonFailed, originalBytes, image: image);
    }

    public static PngAnalysisResult Compared(
        PngImageFacts image,
        PngComparisonMetrics comparison,
        PngRecommendationThresholds thresholds)
    {
        EnsureStaticOpaque(image);
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(thresholds);
        var recommendsWebp = thresholds.RecommendsLosslessWebp(
            comparison.OriginalBytes,
            comparison.NormalizedPngBytes,
            comparison.LosslessWebpBytes);
        return new(
            recommendsWebp
                ? PngImageAnalysisClassification.OpaqueWebpCandidate
                : PngImageAnalysisClassification.OpaqueBelowWebpThreshold,
            comparison.OriginalBytes,
            image: image,
            comparison: comparison,
            recommendation: recommendsWebp ? PngRecommendation.LosslessWebp : PngRecommendation.None);
    }

    private static void EnsureStaticOpaque(PngImageFacts image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.FrameCount != 1 || image.UsesTransparency is not false)
        {
            throw new ArgumentException("The image must be static and opaque.", nameof(image));
        }
    }

    private static bool IsFailureClassification(PngImageAnalysisClassification classification) =>
        classification is PngImageAnalysisClassification.IdentificationFailed
            or PngImageAnalysisClassification.UnsupportedBitDepth
            or PngImageAnalysisClassification.DimensionsExceeded
            or PngImageAnalysisClassification.PixelLimitExceeded
            or PngImageAnalysisClassification.DecodedMemoryExceeded
            or PngImageAnalysisClassification.DecodeFailed;
}

public sealed record PngRecommendationThresholds
{
    public PngRecommendationThresholds(decimal minSavingsPercent, long minSavingsBytes)
    {
        if (minSavingsPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minSavingsPercent));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minSavingsBytes);
        MinSavingsPercent = minSavingsPercent;
        MinSavingsBytes = minSavingsBytes;
    }

    public decimal MinSavingsPercent { get; }

    public long MinSavingsBytes { get; }

    public bool RecommendsLosslessWebp(
        long originalBytes,
        long normalizedPngBytes,
        long losslessWebpBytes) =>
        MeetsThreshold(originalBytes, losslessWebpBytes)
        && MeetsThreshold(normalizedPngBytes, losslessWebpBytes);

    private bool MeetsThreshold(long baselineBytes, long candidateBytes)
    {
        if (baselineBytes <= 0 || candidateBytes < 0)
        {
            return false;
        }

        var savingsBytes = baselineBytes - candidateBytes;
        return savingsBytes >= MinSavingsBytes
            && savingsBytes * 100m >= baselineBytes * MinSavingsPercent;
    }
}

public static class PngAnalysisProfiles
{
    public const string Analyzer = "png-alpha-v1";
    public const string Comparison = "normalized-png-vs-lossless-webp-v1";
}

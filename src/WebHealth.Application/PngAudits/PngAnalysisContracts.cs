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
        CancellationToken cancellationToken = default) =>
        AnalyzeAsync(encodedImage, cancellationToken);
}

public interface IPngFormatComparisonEngine
{
    Task<PngFormatComparisonResult> CompareAsync(
        ReadOnlyMemory<byte> sourcePng,
        PngSourceEncodingFacts sourceFacts,
        PngRecommendationThresholds thresholds,
        CancellationToken cancellationToken);
}

public sealed record PngSourceEncodingFacts(PngColorMeaning ColorMeaning);

public sealed record PngFormatComparisonResult
{
    private PngFormatComparisonResult(
        long? verifiedWebpBytes,
        string profileVersion,
        string? unavailableReason)
    {
        if (verifiedWebpBytes is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verifiedWebpBytes.Value);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(profileVersion);
        if (verifiedWebpBytes is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(unavailableReason);
        }

        VerifiedWebpBytes = verifiedWebpBytes;
        ProfileVersion = profileVersion;
        UnavailableReason = unavailableReason;
    }

    public long? VerifiedWebpBytes { get; }

    public string ProfileVersion { get; }

    public string? UnavailableReason { get; }

    public bool Verified => VerifiedWebpBytes is not null;

    public static PngFormatComparisonResult Success(long verifiedWebpBytes, string profileVersion) =>
        new(verifiedWebpBytes, profileVersion, null);

    public static PngFormatComparisonResult Unavailable(
        string reason,
        string profileVersion) =>
        new(null, profileVersion, reason);
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
    HighBitDepthPng,
    ColorProfileUnsupported,
    ComparisonUnavailable,
    VerifiedWebpCandidate,
    OptimizedPngPreferred,
    BelowWebpThreshold
}

public enum PngRecommendation
{
    None,
    OptimizePng,
    LosslessWebp
}

public enum PngColorMeaning
{
    AssumedSrgb,
    DeclaredSrgb,
    IccProfile,
    NotTransferable
}

public sealed record PngTransparencyFacts
{
    public PngTransparencyFacts(
        long pixelCount,
        long semiTransparentPixelCount,
        long fullyTransparentPixelCount,
        long backgroundTransparentPixelCount,
        long interiorTransparentPixelCount,
        byte minAlpha)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(semiTransparentPixelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(fullyTransparentPixelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(backgroundTransparentPixelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(interiorTransparentPixelCount);
        if (backgroundTransparentPixelCount + interiorTransparentPixelCount
            != fullyTransparentPixelCount)
        {
            throw new ArgumentException(
                "Background and interior transparent pixels must sum to the fully transparent count.",
                nameof(backgroundTransparentPixelCount));
        }
        if (semiTransparentPixelCount + fullyTransparentPixelCount > pixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(semiTransparentPixelCount));
        }
        if (minAlpha == byte.MaxValue
            && semiTransparentPixelCount + fullyTransparentPixelCount > 0)
        {
            throw new ArgumentException(
                "A fully opaque minimum alpha cannot accompany transparent pixels.",
                nameof(minAlpha));
        }

        PixelCount = pixelCount;
        SemiTransparentPixelCount = semiTransparentPixelCount;
        FullyTransparentPixelCount = fullyTransparentPixelCount;
        BackgroundTransparentPixelCount = backgroundTransparentPixelCount;
        InteriorTransparentPixelCount = interiorTransparentPixelCount;
        MinAlpha = minAlpha;
    }

    public long PixelCount { get; }

    public long SemiTransparentPixelCount { get; }

    public long FullyTransparentPixelCount { get; }

    public long BackgroundTransparentPixelCount { get; }

    public long InteriorTransparentPixelCount { get; }

    public byte MinAlpha { get; }

    public long TransparentPixelCount =>
        SemiTransparentPixelCount + FullyTransparentPixelCount;

    public bool UsesTransparency => TransparentPixelCount > 0;

    public decimal TransparentPixelPercent => TransparentPixelCount * 100m / PixelCount;

    public decimal BackgroundCoveragePercent =>
        BackgroundTransparentPixelCount * 100m / PixelCount;

    public bool HasTransparentBackground(decimal minCoveragePercent) =>
        BackgroundTransparentPixelCount > 0
        && BackgroundCoveragePercent >= minCoveragePercent;
}

public sealed record PngImageFacts
{
    public PngImageFacts(
        int width,
        int height,
        int frameCount,
        long pixelCount,
        byte bitDepth,
        byte colorType,
        PngTransparencyFacts? transparency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelCount);
        if (pixelCount != checked((long)width * height))
        {
            throw new ArgumentException(
                "The pixel count must match the image dimensions.",
                nameof(pixelCount));
        }
        if (transparency is not null && transparency.PixelCount != pixelCount)
        {
            throw new ArgumentException(
                "Transparency facts must describe the same pixel count.",
                nameof(transparency));
        }

        Width = width;
        Height = height;
        FrameCount = frameCount;
        PixelCount = pixelCount;
        BitDepth = bitDepth;
        ColorType = colorType;
        Transparency = transparency;
    }

    public int Width { get; }

    public int Height { get; }

    public int FrameCount { get; }

    public long PixelCount { get; }

    public byte BitDepth { get; }

    public byte ColorType { get; }

    public PngTransparencyFacts? Transparency { get; }

    public bool? UsesTransparency => Transparency?.UsesTransparency;

    public long? TransparentPixelCount => Transparency?.TransparentPixelCount;

    public decimal? TransparentPixelPercent => Transparency?.TransparentPixelPercent;
}

public sealed record PngComparisonMetrics
{
    public PngComparisonMetrics(
        long originalPngBytes,
        long candidateWebpBytes,
        long? optimizedPngBytes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(originalPngBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateWebpBytes);
        if (optimizedPngBytes is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(optimizedPngBytes.Value);
        }
        OriginalPngBytes = originalPngBytes;
        OptimizedPngBytes = optimizedPngBytes;
        CandidateWebpBytes = candidateWebpBytes;
    }

    public long OriginalPngBytes { get; }

    public long? OptimizedPngBytes { get; }

    public long CandidateWebpBytes { get; }

    public long? ReferencePngBytes => OptimizedPngBytes is { } optimized
        ? Math.Min(OriginalPngBytes, optimized)
        : null;

    public long OriginalSavingsBytes => OriginalPngBytes - CandidateWebpBytes;

    public decimal OriginalSavingsPercent => OriginalSavingsBytes * 100m / OriginalPngBytes;

    public long? ReferenceSavingsBytes => ReferencePngBytes - CandidateWebpBytes;

    public decimal? ReferenceSavingsPercent =>
        ReferenceSavingsBytes * 100m / ReferencePngBytes;
}

public sealed record PngAnalysisResult
{
    private PngAnalysisResult(
        PngImageAnalysisClassification classification,
        long originalBytes,
        string? detectedFormat = null,
        PngImageFacts? image = null,
        PngComparisonMetrics? comparison = null,
        PngRecommendation recommendation = PngRecommendation.None,
        string? unavailableReason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalBytes);
        Classification = classification;
        OriginalBytes = originalBytes;
        DetectedFormat = detectedFormat;
        Image = image;
        Comparison = comparison;
        Recommendation = recommendation;
        UnavailableReason = unavailableReason;
    }

    public PngImageAnalysisClassification Classification { get; }

    public long OriginalBytes { get; }

    public string? DetectedFormat { get; }

    public PngImageFacts? Image { get; }

    public PngComparisonMetrics? Comparison { get; }

    public PngRecommendation Recommendation { get; }

    public string? UnavailableReason { get; }

    public static PngAnalysisResult NotPng(long originalBytes, string detectedFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedFormat);
        var normalizedFormat = detectedFormat.Trim();
        if (string.Equals(normalizedFormat, "PNG", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The detected format must not be PNG.",
                nameof(detectedFormat));
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
        if (image.FrameCount <= 1 || image.Transparency is not null)
        {
            throw new ArgumentException(
                "An animated PNG must have multiple frames and unmeasured transparency.",
                nameof(image));
        }

        return new(PngImageAnalysisClassification.AnimatedPng, originalBytes, image: image);
    }

    public static PngAnalysisResult HighBitDepth(long originalBytes, PngImageFacts image)
    {
        EnsureStaticMeasured(image);
        if (image.BitDepth <= 8)
        {
            throw new ArgumentException(
                "A high-bit-depth result requires a bit depth above eight.",
                nameof(image));
        }

        return new(PngImageAnalysisClassification.HighBitDepthPng, originalBytes, image: image);
    }

    public static PngAnalysisResult ColorProfileUnsupported(long originalBytes, PngImageFacts image)
    {
        EnsureStaticMeasured(image);
        return new(
            PngImageAnalysisClassification.ColorProfileUnsupported,
            originalBytes,
            image: image);
    }

    public static PngAnalysisResult ComparisonUnavailable(
        long originalBytes,
        PngImageFacts image,
        PngComparisonMetrics? comparison = null,
        string unavailableReason = "ComparisonEngineUnavailable")
    {
        EnsureStaticMeasured(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(unavailableReason);
        return new(
            PngImageAnalysisClassification.ComparisonUnavailable,
            originalBytes,
            image: image,
            comparison: comparison,
            unavailableReason: unavailableReason);
    }

    public static PngAnalysisResult ComparedAgainstOriginal(
        long originalBytes,
        PngImageFacts image,
        long verifiedWebpBytes,
        PngRecommendationThresholds thresholds)
    {
        EnsureStaticMeasured(image);
        ArgumentNullException.ThrowIfNull(thresholds);
        var comparison = new PngComparisonMetrics(originalBytes, verifiedWebpBytes);
        if (!thresholds.MeetsThreshold(originalBytes, verifiedWebpBytes))
        {
            return new(
                PngImageAnalysisClassification.BelowWebpThreshold,
                originalBytes,
                image: image,
                comparison: comparison);
        }

        return new(
            PngImageAnalysisClassification.ComparisonUnavailable,
            originalBytes,
            image: image,
            comparison: comparison,
            unavailableReason: "OptimizedPngReferencePending");
    }

    private static void EnsureStaticMeasured(PngImageFacts image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.FrameCount != 1 || image.Transparency is null)
        {
            throw new ArgumentException(
                "The image must be static with measured transparency.",
                nameof(image));
        }
    }

    private static bool IsFailureClassification(PngImageAnalysisClassification classification) =>
        classification is PngImageAnalysisClassification.IdentificationFailed
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

    public bool MeetsThreshold(long baselineBytes, long candidateBytes)
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
    public const string Analyzer = "png-alpha-v2";

    public const string Comparison = VerifiedComparison;

    public const string UnverifiedComparison = "normalized-png-vs-lossless-webp-v1";

    public const string VerifiedComparison = "libwebp-exact-vs-optimized-png-v2";

    public const string LegacyAnalyzer = "png-alpha-v1";

    public static bool IsVerifiedComparison(string comparisonProfile) =>
        string.Equals(comparisonProfile, VerifiedComparison, StringComparison.Ordinal);
}

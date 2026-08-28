using WebHealth.Application.PngAudits;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class PngRecommendationThresholdTests
{
    [Theory]
    [InlineData(50000, 45000, true)]
    [InlineData(44000, 40000, false)]
    [InlineData(10000, 8000, false)]
    [InlineData(50000, 51000, false)]
    public void MeetsThreshold_RequiresBothByteAndPercentageSavings(
        long baselineBytes,
        long candidateBytes,
        bool expected)
    {
        var thresholds = new PngRecommendationThresholds(10, 4096);

        Assert.Equal(expected, thresholds.MeetsThreshold(baselineBytes, candidateBytes));
    }

    [Theory]
    [InlineData(-1, 4096)]
    [InlineData(101, 4096)]
    [InlineData(10, -1)]
    public void Constructor_RejectsInvalidThresholds(
        decimal minSavingsPercent,
        long minSavingsBytes)
    {
        var act = () => new PngRecommendationThresholds(minSavingsPercent, minSavingsBytes);

        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void ReferencePngBytes_UsesTheSmallerOfTheOriginalAndOptimizedPng()
    {
        var comparison = new PngComparisonMetrics(50000, 40000, 44000);

        Assert.Equal(44000, comparison.ReferencePngBytes);
        Assert.Equal(10000, comparison.OriginalSavingsBytes);
        Assert.Equal(4000, comparison.ReferenceSavingsBytes);
    }

    [Fact]
    public void ComparisonUnavailable_AcceptsATransparentImage()
    {
        var facts = new PngImageFacts(
            100, 100, 1, 10000, 8, 6,
            new PngTransparencyFacts(10000, 500, 2000, 1800, 200, 0));

        var result = PngAnalysisResult.ComparisonUnavailable(
            50000,
            facts,
            new PngComparisonMetrics(50000, 40000, 44000));

        Assert.Equal(PngImageAnalysisClassification.ComparisonUnavailable, result.Classification);
        Assert.Equal(PngRecommendation.None, result.Recommendation);
        Assert.True(result.Image!.UsesTransparency);
        Assert.NotNull(result.Comparison);
    }

    [Fact]
    public void ComparedAgainstReference_RecommendsWebpOnlyWhenItBeatsTheReference()
    {
        var result = PngAnalysisResult.ComparedAgainstReference(
            50000,
            StaticFacts(),
            35000,
            44000,
            new PngRecommendationThresholds(10, 4096));

        Assert.Equal(PngImageAnalysisClassification.VerifiedWebpCandidate, result.Classification);
        Assert.Equal(PngRecommendation.LosslessWebp, result.Recommendation);
        Assert.Equal(44000, result.Comparison!.ReferencePngBytes);
    }

    [Fact]
    public void ComparedAgainstReference_PrefersTheOptimizedPngWhenWebpMissesTheReferenceThreshold()
    {
        var result = PngAnalysisResult.ComparedAgainstReference(
            50000,
            StaticFacts(),
            41000,
            42000,
            new PngRecommendationThresholds(10, 4096));

        Assert.Equal(PngImageAnalysisClassification.OptimizedPngPreferred, result.Classification);
        Assert.Equal(PngRecommendation.OptimizePng, result.Recommendation);
    }

    private static PngImageFacts StaticFacts() =>
        new(
            100,
            100,
            1,
            10000,
            8,
            6,
            new PngTransparencyFacts(10000, 0, 0, 0, 0, byte.MaxValue));

    [Fact]
    public void TransparencyFacts_RejectMismatchedRegionCounts()
    {
        var act = () => new PngTransparencyFacts(10000, 500, 2000, 1800, 100, 0);

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void TransparencyFacts_RejectOpaqueMinimumAlphaWithTransparentPixels()
    {
        var act = () => new PngTransparencyFacts(10000, 500, 0, 0, 0, byte.MaxValue);

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void TransparencyFacts_AcceptTheNarrowestNonOpaqueAlpha()
    {
        var facts = new PngTransparencyFacts(10000, 1, 0, 0, 0, byte.MaxValue - 1);

        Assert.True(facts.UsesTransparency);
        Assert.Equal(byte.MaxValue - 1, facts.MinAlpha);
    }

    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(9305, 8941, 60.5759)]
    public void BackgroundCoverage_IsMeasuredAgainstThePixelCount(
        long fullyTransparent,
        long background,
        double expectedPercent)
    {
        var facts = new PngTransparencyFacts(
            14760,
            0,
            fullyTransparent,
            background,
            fullyTransparent - background,
            fullyTransparent > 0 ? (byte)0 : byte.MaxValue);

        Assert.Equal((decimal)expectedPercent, Math.Round(facts.BackgroundCoveragePercent, 4));
    }

    [Fact]
    public void AntiAliasedEdges_AreNotATransparentBackground()
    {
        var facts = new PngTransparencyFacts(106150, 772, 0, 0, 0, 241);

        Assert.True(facts.UsesTransparency);
        Assert.Equal(0m, facts.BackgroundCoveragePercent);
        Assert.False(facts.HasTransparentBackground(1.0m));
    }

    [Fact]
    public void ACutOutLogo_IsATransparentBackground()
    {
        var facts = new PngTransparencyFacts(14760, 2832, 9305, 8941, 364, 0);

        Assert.True(facts.HasTransparentBackground(1.0m));
        Assert.Equal(364, facts.InteriorTransparentPixelCount);
    }

    [Fact]
    public void Animated_RejectsSingleFrameFacts()
    {
        var act = () => PngAnalysisResult.Animated(
            100,
            new PngImageFacts(1, 1, 1, 1, 8, 6, null));

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Animated_RejectsManufacturedTransparencyFacts()
    {
        var act = () => PngAnalysisResult.Animated(
            100,
            new PngImageFacts(
                1, 1, 2, 1, 8, 6,
                new PngTransparencyFacts(1, 0, 0, 0, 0, byte.MaxValue)));

        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData(0, 1, 1, 1, 1)]
    [InlineData(1, 0, 1, 1, 1)]
    [InlineData(1, 1, 0, 1, 1)]
    [InlineData(1, 1, 1, 0, 1)]
    [InlineData(1, 1, 1, 1, 0)]
    public void AnalysisLimits_RejectNonPositiveValues(
        int maxEncodedBytes,
        int maxWidth,
        int maxHeight,
        long maxDecodedPixels,
        long maxDecodedMemoryBytes)
    {
        var act = () => new PngImageAnalysisLimits(
            maxEncodedBytes,
            maxWidth,
            maxHeight,
            maxDecodedPixels,
            maxDecodedMemoryBytes);

        Assert.Throws<ArgumentOutOfRangeException>(act);
    }
}

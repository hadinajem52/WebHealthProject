using WebHealth.Application.PngAudits;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class PngRecommendationThresholdTests
{
    [Theory]
    [InlineData(50000, 50000, 45000, true)]
    [InlineData(100000, 44000, 40000, false)]
    [InlineData(10000, 10000, 8000, false)]
    [InlineData(50000, 50000, 51000, false)]
    public void RecommendsLosslessWebp_RequiresBothThresholdsAgainstBothBaselines(
        long originalBytes,
        long normalizedPngBytes,
        long losslessWebpBytes,
        bool expected)
    {
        var thresholds = new PngRecommendationThresholds(10, 4096);

        Assert.Equal(expected, thresholds.RecommendsLosslessWebp(
            originalBytes,
            normalizedPngBytes,
            losslessWebpBytes));
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
    public void Compared_DerivesAConsistentCandidateResult()
    {
        var result = PngAnalysisResult.Compared(
            new PngImageFacts(100, 100, 1, 10000, 0),
            new PngComparisonMetrics(50000, 50000, 40000),
            new PngRecommendationThresholds(10, 4096));

        Assert.Equal(PngImageAnalysisClassification.OpaqueWebpCandidate, result.Classification);
        Assert.Equal(PngRecommendation.LosslessWebp, result.Recommendation);
        Assert.NotNull(result.Image);
        Assert.NotNull(result.Comparison);
    }

    [Fact]
    public void Animated_RejectsSingleFrameFacts()
    {
        var act = () => PngAnalysisResult.Animated(
            100,
            new PngImageFacts(1, 1, 1, 1, null));

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Animated_RejectsManufacturedTransparencyFacts()
    {
        var act = () => PngAnalysisResult.Animated(
            100,
            new PngImageFacts(1, 1, 2, 1, 0));

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

using FluentAssertions;
using WebHealth.Application.Reporting;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class ResponseTimeHistogramTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 2)]
    [InlineData(120000, 10)]
    [InlineData(120001, 11)]
    [InlineData(int.MaxValue, 11)]
    public void BucketBoundsAreInclusive(int duration, int bucket) =>
        ResponseTimeHistogram.BucketIndex(duration).Should().Be(bucket);

    [Fact]
    public void EstimatesUseNearestRankAndRecordedMaximum()
    {
        var counts = new long[ResponseTimeHistogram.BucketCount];
        foreach (var duration in new[] { 0, 20, 100, 101, 200, 400, 700, 2000, 6000, 125000 })
            counts[ResponseTimeHistogram.BucketIndex(duration)]++;
        ResponseTimeHistogram.EstimatePercentile(counts, 0, 125000).Should().Be(0);
        ResponseTimeHistogram.EstimatePercentile(counts, 0.5, 125000).Should().Be(250);
        ResponseTimeHistogram.EstimatePercentile(counts, 0.95, 125000).Should().Be(125000);
        ResponseTimeHistogram.EstimatePercentile(counts, 1, 125000).Should().Be(125000);
        Array.Clear(counts);
        counts[ResponseTimeHistogram.BucketIndex(30)] = 1;
        ResponseTimeHistogram.EstimatePercentile(counts, 0.95, 30).Should().Be(30);
        Array.Clear(counts);
        ResponseTimeHistogram.EstimatePercentile(counts, 0.95, 0).Should().BeNull();
    }

    [Fact]
    public void InvalidStoredCountsAndPercentilesAreRejected()
    {
        var counts = new long[ResponseTimeHistogram.BucketCount];
        var wrongShape = () => ResponseTimeHistogram.EstimatePercentile([], 0.5, 0);
        wrongShape.Should().Throw<ArgumentException>();
        counts[0] = -1;
        var negativeCount = () => ResponseTimeHistogram.EstimatePercentile(counts, 0.5, 0);
        negativeCount.Should().Throw<ArgumentOutOfRangeException>();
        counts[0] = 0;
        foreach (var percentile in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
        {
            var invalid = () => ResponseTimeHistogram.EstimatePercentile(counts, percentile, 0);
            invalid.Should().Throw<ArgumentOutOfRangeException>();
        }
        var negativeDuration = () => ResponseTimeHistogram.BucketIndex(-1);
        negativeDuration.Should().Throw<ArgumentOutOfRangeException>();
    }
}

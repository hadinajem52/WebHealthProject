using WebHealth.Application.Monitoring;
using WebHealth.Application.SiteAnalysis;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class SiteAnalysisFetchProfileTests
{
    [Fact]
    public void Constructor_RejectsResponseLimitsReservedForPngImages()
    {
        var act = () => new SiteAnalysisFetchProfile(
            SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes + 1,
            15,
            2,
            1,
            TimeSpan.Zero,
            TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Constructor_RejectsUnsafeRetryCounts(int retryCount)
    {
        var act = () => new SiteAnalysisFetchProfile(
            1024,
            15,
            2,
            retryCount,
            TimeSpan.Zero,
            TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    [InlineData(double.NaN)]
    public void Constructor_RejectsUnsafeHostRates(double requestsPerSecondPerHost)
    {
        var act = () => new SiteAnalysisFetchProfile(
            1024,
            15,
            requestsPerSecondPerHost,
            1,
            TimeSpan.Zero,
            TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Constructor_PreservesTheConsumerRatePolicy()
    {
        var profile = new SiteAnalysisFetchProfile(
            1024,
            15,
            3.5,
            1,
            TimeSpan.Zero,
            TimeSpan.Zero);

        Assert.Equal(3.5, profile.RequestsPerSecondPerHost);
    }
}

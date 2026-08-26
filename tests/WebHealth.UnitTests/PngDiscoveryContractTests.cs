using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class PngDiscoveryContractTests
{
    [Fact]
    public void PageLimits_RejectAResponseCeilingAboveTheSafeTransportDefault()
    {
        var act = () => new PngPageDiscoveryLimits(
            10,
            2,
            SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes + 1,
            10L * 1024 * 1024,
            100);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ImageLimits_RequireEnoughCapacityForEveryUniqueImageMapping()
    {
        var act = () => new PngImageDiscoveryLimits(10, 9);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    [InlineData(double.NaN)]
    public void FetchPolicy_RejectsUnsafeHostRates(double requestsPerSecondPerHost)
    {
        var act = () => new PngDiscoveryFetchPolicy(
            100,
            15,
            requestsPerSecondPerHost,
            1,
            TimeSpan.FromMinutes(30));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Scope_RequiresSeparatePageAndAssetHostSets()
    {
        var act = () => new PngSiteDiscoveryScope(
            "https://site.test/",
            [new CrawlHostRule("site.test")],
            ["/"],
            [],
            CrawlUrlOptions.Default);

        act.Should().Throw<ArgumentException>();
    }
}

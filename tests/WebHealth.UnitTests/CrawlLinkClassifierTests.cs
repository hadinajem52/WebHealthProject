using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class CrawlLinkClassifierTests
{
    private static string Classify(
        CrawlRequestOutcome outcome = CrawlRequestOutcome.Responded,
        int? statusCode = null,
        int redirectCount = 0) =>
        CrawlLinkClassifier.Classify(new(outcome, statusCode, redirectCount));

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    public void Classify_CallsADirectSuccessHealthy(int statusCode) =>
        Classify(statusCode: statusCode).Should().Be(CrawlLinkClassifications.Healthy);

    [Fact]
    public void Classify_CallsASuccessReachedThroughARedirectRedirected() =>
        Classify(statusCode: 200, redirectCount: 1).Should().Be(CrawlLinkClassifications.Redirected,
            "the link works and is also stale; only reporting the second gets it fixed");

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public void Classify_CallsAnErrorStatusBroken(int statusCode) =>
        Classify(statusCode: statusCode).Should().Be(CrawlLinkClassifications.Broken);

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Classify_LeavesATransientStatusUnknown(int statusCode) =>
        Classify(statusCode: statusCode).Should().Be(CrawlLinkClassifications.Unknown);

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(407)]
    [InlineData(451)]
    public void Classify_CallsAnAuthorizationStatusBlockedRatherThanBroken(int statusCode) =>
        Classify(statusCode: statusCode).Should().Be(CrawlLinkClassifications.Blocked,
            "the resource exists; calling this broken would report every authenticated area");

    [Fact]
    public void Classify_CallsARedirectThatNeverLandedBroken() =>
        Classify(statusCode: 302, redirectCount: 10).Should().Be(CrawlLinkClassifications.Broken,
            "a 3xx after the redirect budget was spent never reached a resource");

    [Fact]
    public void Classify_SeparatesATimeoutFromABrokenLink() =>
        Classify(CrawlRequestOutcome.Timeout).Should().Be(CrawlLinkClassifications.Timeout);

    [Fact]
    public void Classify_CallsAPolicyRefusalBlocked() =>
        Classify(CrawlRequestOutcome.Blocked).Should().Be(CrawlLinkClassifications.Blocked);

    [Fact]
    public void Classify_LeavesATransportFailureUnknown() =>
        Classify(CrawlRequestOutcome.Failed).Should().Be(CrawlLinkClassifications.Unknown);

    [Fact]
    public void Classify_CallsADeterministicRequestFailureBroken() =>
        Classify(CrawlRequestOutcome.Broken).Should().Be(CrawlLinkClassifications.Broken);

    [Fact]
    public void Classify_CallsAResponseWithNoStatusUnknown() =>
        Classify().Should().Be(CrawlLinkClassifications.Unknown);
}

using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>BR-L02. Robots is respected by default, and an override needs all three conditions.</summary>
public sealed class CrawlRobotsGateTests
{
    private const string Agent = "webhealthmonitor/1.0";

    private static CrawlRobotsFacts Blocking(bool approved = false) =>
        new(true, "User-agent: *\nDisallow: /private", approved);

    [Fact]
    public void IsAllowed_ObeysADisallowFromTheStoredSnapshot()
    {
        CrawlRobotsGate.IsAllowed(Blocking(), Agent, "/private/x", false).Should().BeFalse();
        CrawlRobotsGate.IsAllowed(Blocking(), Agent, "/public/x", false).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_CrawlsAnOriginWithNoSnapshot() =>
        CrawlRobotsGate.IsAllowed(CrawlRobotsFacts.Unknown, Agent, "/private/x", false).Should()
            .BeTrue("absence of evidence is not a prohibition, and a stalled refresh must not stop every crawl");

    [Fact]
    public void IsAllowed_CrawlsWhenTheSnapshotHasNoGroups() =>
        CrawlRobotsGate.IsAllowed(new(true, "# nothing here", false), Agent, "/x", false)
            .Should().BeTrue();

    [Fact]
    public void IsAllowed_FollowsTheOverrideOnceItIsGranted() =>
        CrawlRobotsGate.IsAllowed(Blocking(approved: true), Agent, "/private/x", true).Should().BeTrue();

    [Fact]
    public void EvaluateOverride_GrantsOnlyForAnApprovedNonProductionOrigin() =>
        CrawlRobotsGate.EvaluateOverride(true, false, Blocking(approved: true))
            .Should().Be(new CrawlOverrideDecision(true, null));

    [Theory]
    [InlineData(false, false, true, CrawlOverrideRefusals.NotRequested)]
    [InlineData(true, true, true, CrawlOverrideRefusals.ProductionTarget)]
    [InlineData(true, false, false, CrawlOverrideRefusals.NoApprovedException)]
    public void EvaluateOverride_RefusesAndSaysWhy(
        bool requested,
        bool isProduction,
        bool approved,
        string expected) =>
        CrawlRobotsGate.EvaluateOverride(requested, isProduction, Blocking(approved))
            .Should().Be(CrawlOverrideDecision.Refused(expected));

    [Fact]
    public void EvaluateOverride_RefusesAProductionTargetEvenWithAnApprovedException() =>
        CrawlRobotsGate.EvaluateOverride(true, true, Blocking(approved: true))
            .Granted.Should().BeFalse("a production crawl never bypasses a published restriction");
}

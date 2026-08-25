using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

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
    public void EvaluateOverride_GrantsWheneverTheRunAsks() =>
        CrawlRobotsGate.EvaluateOverride(true).Should().Be(new CrawlOverrideDecision(true, null));

    [Fact]
    public void EvaluateOverride_RefusesARunThatDidNotAsk() =>
        CrawlRobotsGate.EvaluateOverride(false)
            .Should().Be(CrawlOverrideDecision.Refused(CrawlOverrideRefusals.NotRequested),
                "the override is applied because a run asked for it, never by default");
}

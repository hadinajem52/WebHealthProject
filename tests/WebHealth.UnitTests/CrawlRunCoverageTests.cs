using FluentAssertions;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class CrawlRunCoverageTests
{
    private static CrawlRunSummary Run(
        string status,
        string stopReason,
        int pagesFetched,
        bool coverageLimited = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, stopReason, pagesFetched,
            LinksRecorded: 1, BrokenLinkCount: 0, RobotsOverrideGranted: false,
            RobotsOverrideRefusedBecause: CrawlOverrideRefusals.NotRequested,
            StartedAt: DateTimeOffset.UnixEpoch, FinishedAt: DateTimeOffset.UnixEpoch,
            CoverageLimited: coverageLimited);

    [Fact]
    public void CompletedRunThatFetchedPages_CoveredTheWholeScope()
    {
        var run = Run(CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted, pagesFetched: 42);

        run.CoveredWholeScope.Should().BeTrue();
    }

    [Fact]
    public void RunRefusedAtEveryDoor_DidNotCoverTheWholeScope()
    {
        var run = Run(CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted, pagesFetched: 0);

        run.CoveredWholeScope.Should().BeFalse(
            "an exhausted frontier with nothing fetched examined nothing, so it must not stand as "
            + "a clean result or as a comparison baseline");
    }

    [Fact]
    public void RunThatCouldNotReadEveryPage_DidNotCoverTheWholeScope()
    {
        var run = Run(
            CrawlRunStatuses.Completed,
            CrawlStopReasons.FrontierExhausted,
            pagesFetched: 42,
            coverageLimited: true);

        run.CoveredWholeScope.Should().BeFalse(
            "a page nobody could read contributes no links, so this run must not stand as the "
            + "baseline a later comparison calls links resolved against");
    }

    [Theory]
    [InlineData(CrawlStopReasons.PageLimit)]
    [InlineData(CrawlStopReasons.DurationLimit)]
    [InlineData(CrawlStopReasons.Cancelled)]
    public void RunStoppedOnABudget_DidNotCoverTheWholeScope(string stopReason)
    {
        var status = stopReason == CrawlStopReasons.Cancelled
            ? CrawlRunStatuses.Cancelled
            : CrawlRunStatuses.Completed;

        Run(status, stopReason, pagesFetched: 900).CoveredWholeScope.Should().BeFalse();
    }

    [Fact]
    public void FailedRun_DidNotCoverTheWholeScope()
    {
        Run(CrawlRunStatuses.Failed, CrawlStopReasons.Failed, pagesFetched: 5)
            .CoveredWholeScope.Should().BeFalse();
    }
}

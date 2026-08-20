using FluentAssertions;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// Phase 6. What "covered the whole scope" is allowed to mean.
/// <para>
/// These exist because a robots-disallowed crawl reproduced the failure the comparison was built
/// to prevent: it drained its frontier without fetching a page, counted as full scope, became the
/// baseline, and reported every previously broken link as resolved.
/// </para>
/// </summary>
public sealed class CrawlRunCoverageTests
{
    private static CrawlRunSummary Run(string status, string stopReason, int pagesFetched) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, stopReason, pagesFetched,
            LinksRecorded: 1, BrokenLinkCount: 0, RobotsOverrideGranted: false,
            StartedAt: DateTimeOffset.UnixEpoch, FinishedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void CompletedRunThatFetchedPages_CoveredTheWholeScope()
    {
        var run = Run(CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted, pagesFetched: 42);

        run.CoveredWholeScope.Should().BeTrue();
    }

    /// <summary>
    /// The regression. Every request was refused — robots disallowing the origin, or no authorized
    /// target — so the frontier drained with nothing fetched. The stop reason alone cannot tell
    /// that apart from a real sweep, which is why the page count is part of the test.
    /// </summary>
    [Fact]
    public void RunRefusedAtEveryDoor_DidNotCoverTheWholeScope()
    {
        var run = Run(CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted, pagesFetched: 0);

        run.CoveredWholeScope.Should().BeFalse(
            "an exhausted frontier with nothing fetched examined nothing, so it must not stand as "
            + "a clean result or as a comparison baseline");
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

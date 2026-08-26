using FluentAssertions;
using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class CrawlExecutionTests
{
    private const string Seed = CrawlTestHarness.Seed;

    private static FakeSiteTransport Site() => new();

    private const string Blocking = "User-agent: *\nDisallow: /private";

    [Fact]
    public async Task ExecuteAsync_ReportsCoverageAsLimitedWhenAPageCouldNotBeRead()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/cut"))
            .With("https://site.test/cut", new(
                200, CrawlTestHarness.LinkTo("/deeper"), Truncated: true));

        var (outcome, _) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        outcome.StopReason.Should().Be(CrawlStopReasons.FrontierExhausted);
        outcome.CoverageLimited.Should().BeTrue(
            "a page whose body was cut short contributed none of the links it holds");
    }

    [Fact]
    public async Task ExecuteAsync_ReportsCoverageAsLimitedWhenRobotsKeptItOutOfAPage()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/private/page"))
            .Page("https://site.test/private/page", CrawlTestHarness.LinkTo("/deeper"));

        var (outcome, _) = await CrawlTestHarness.RunAsync(
            site,
            CrawlTestHarness.Request(),
            robotsReader: new FakeRobotsReader(new(true, Blocking, false)));

        outcome.CoverageLimited.Should().BeTrue(
            "the disallowed URL may be a page, and its links are missing rather than merely unchecked");
    }

    [Fact]
    public async Task ExecuteAsync_LeavesCoverageUnlimitedForARunThatReadEveryPage()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/good", "/gone", "https://external.test/x"))
            .Page("https://site.test/good", CrawlTestHarness.LinkTo())
            .Status("https://site.test/gone", 404);

        var (outcome, _) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        outcome.StopReason.Should().Be(CrawlStopReasons.FrontierExhausted);
        outcome.CoverageLimited.Should().BeFalse(
            "an unchecked external link is recorded and classified, not missing");
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesLinksAgainstTheUrlTheDocumentCameFrom()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/old"))
            .With("https://site.test/old", new(
                200,
                CrawlTestHarness.LinkTo("sibling"),
                RedirectCount: 1,
                FinalUrl: "https://site.test/docs/page"))
            .Page("https://site.test/docs/sibling", CrawlTestHarness.LinkTo());

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        sink.Links.Select(link => link.TargetUrl).Should()
            .Contain("https://site.test/docs/sibling",
                "the relative href belongs to the document at /docs/page")
            .And.NotContain("https://site.test/sibling",
                "that target exists only if the redirect is ignored");
    }

    [Fact]
    public async Task ExecuteAsync_AttributesLinksToTheRedirectedDocument()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/old"))
            .With("https://site.test/old", new(
                200,
                CrawlTestHarness.LinkTo("/gone"),
                RedirectCount: 1,
                FinalUrl: "https://site.test/docs/page"))
            .Status("https://site.test/gone", 404);

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        sink.Links.Should()
            .ContainSingle(link => link.TargetUrl == "https://site.test/gone")
            .Which.SourceUrl.Should().Be("https://site.test/docs/page");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotFollowLinksOfADocumentThatRedirectedOutOfScope()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/out"))
            .With("https://site.test/out", new(
                200,
                CrawlTestHarness.LinkTo("/secret", "https://elsewhere.test/x"),
                RedirectCount: 1,
                FinalUrl: "https://elsewhere.test/landing"));

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        sink.Links.Select(link => link.TargetUrl).Should()
            .NotContain("https://elsewhere.test/secret")
            .And.NotContain("https://elsewhere.test/x")
            .And.NotContain("https://site.test/secret");
        site.Requested.Should().NotContain("https://elsewhere.test/secret");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotFollowADocumentRobotsDisallowsAtItsFinalUrl()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/open"))
            .With("https://site.test/open", new(
                200,
                CrawlTestHarness.LinkTo("/private/deeper"),
                RedirectCount: 1,
                FinalUrl: "https://site.test/private/landing"));

        var (_, sink) = await CrawlTestHarness.RunAsync(
            site,
            CrawlTestHarness.Request(),
            robotsReader: new FakeRobotsReader(new(true, Blocking, false)));

        sink.Links.Select(link => link.TargetUrl).Should()
            .NotContain("https://site.test/private/deeper");
    }

    [Fact]
    public async Task ExecuteAsync_RecordsWhyAFailedRunFailed()
    {
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo("/a"));
        site.BeforeRespondAsync = _ =>
            throw new InvalidOperationException("the connection pool is exhausted");

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        outcome.Status.Should().Be(CrawlRunStatuses.Failed);
        outcome.StopReason.Should().Be(CrawlStopReasons.Failed);
        outcome.FailureCode.Should().Be(
            CrawlFailureCodes.Unexpected,
            "an unclassified fault still has to be recorded as something");
        sink.Outcome!.FailureCode.Should().Be(outcome.FailureCode,
            "the reason has to reach the store, not only the caller");
        outcome.FailureCode.Should().NotContain("Exception",
            "the report shows written copy; the exception belongs in the log");
        outcome.FailureCode.Should().NotContain("connection pool",
            "an exception message must never reach a page the user reads");
    }

    [Fact]
    public async Task ExecuteAsync_LeavesNoFailureCodeOnARunThatCompleted()
    {
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo());

        var (outcome, _) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        outcome.Status.Should().Be(CrawlRunStatuses.Completed);
        outcome.FailureCode.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ReportsABrokenInternalLinkWithItsSourcePage()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/good", "/gone"))
            .Page("https://site.test/good", CrawlTestHarness.LinkTo())
            .Status("https://site.test/gone", 404);

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        outcome.Status.Should().Be(CrawlRunStatuses.Completed);
        outcome.StopReason.Should().Be(CrawlStopReasons.FrontierExhausted);

        var broken = sink.Links.Should()
            .ContainSingle(link => link.Classification == CrawlLinkClassifications.Broken).Subject;
        broken.TargetUrl.Should().Be("https://site.test/gone");
        broken.SourceUrl.Should().Be(Seed, "the report is only actionable with the page that links to it");
        broken.StatusCode.Should().Be(404);
        broken.IsInternal.Should().BeTrue();
        broken.Depth.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsABrokenLinkOncePerSourceTargetPair()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/a", "/b"))
            .Page("https://site.test/a", CrawlTestHarness.LinkTo("/gone", "/gone", "/gone"))
            .Page("https://site.test/b", CrawlTestHarness.LinkTo("/gone"))
            .Status("https://site.test/gone", 404);

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        var broken = sink.Links.Where(link => link.TargetUrl == "https://site.test/gone").ToArray();

        broken.Should().HaveCount(2, "BR-L07: one result per source-target pair, not per link tag");
        broken.Select(link => link.SourceUrl).Should()
            .BeEquivalentTo(["https://site.test/a", "https://site.test/b"]);
        broken.Should().OnlyContain(link => link.Classification == CrawlLinkClassifications.Broken);
    }

    [Fact]
    public async Task ExecuteAsync_FetchesEachTargetOnceHoweverManyPagesLinkToIt()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/a", "/b"))
            .Page("https://site.test/a", CrawlTestHarness.LinkTo("/shared"))
            .Page("https://site.test/b", CrawlTestHarness.LinkTo("/shared"))
            .Page("https://site.test/shared", CrawlTestHarness.LinkTo("/a", "/b"));

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Count(url => url == "https://site.test/shared").Should().Be(1);
        sink.Links.Count(link => link.TargetUrl == "https://site.test/shared").Should()
            .Be(2, "both source pages still appear in the report");
    }

    [Fact]
    public async Task ExecuteAsync_ChecksAnExternalLinkWithoutCrawlingIt()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("https://other.test/landing"))
            .Page("https://other.test/landing", CrawlTestHarness.LinkTo("/deep-one", "/deep-two"));

        var (_, sink) = await CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request() with { CheckExternalLinks = true });

        site.Requested.Should().Contain("https://other.test/landing");
        site.Requested.Should().NotContain(url => url.StartsWith("https://other.test/deep", StringComparison.Ordinal));
        sink.Links.Should().ContainSingle(link => link.TargetUrl == "https://other.test/landing")
            .Which.IsInternal.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRequestAnExternalTargetWhenCheckingIsOff()
    {
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo("https://other.test/landing"));

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Should().NotContain("https://other.test/landing");
        sink.Links.Should().ContainSingle(link => link.TargetUrl == "https://other.test/landing")
            .Which.SkipReason.Should().Be(CrawlSkipReasons.ExternalCheckDisabled);
    }

    [Fact]
    public async Task ExecuteAsync_ClassifiesRedirectedAndFailedTargets()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/moved", "/dns-dead", "/slow", "/private"))
            .With("https://site.test/moved", new(200, RedirectCount: 1))
            .Failing("https://site.test/dns-dead", SafeHttpFailureKind.NameResolution)
            .Failing("https://site.test/slow", SafeHttpFailureKind.Timeout)
            .Status("https://site.test/private", 403);

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        Classification(sink, "https://site.test/moved").Should().Be(CrawlLinkClassifications.Redirected);
        Classification(sink, "https://site.test/dns-dead").Should().Be(CrawlLinkClassifications.Unknown);
        Classification(sink, "https://site.test/slow").Should().Be(CrawlLinkClassifications.Timeout);
        Classification(sink, "https://site.test/private").Should().Be(CrawlLinkClassifications.Blocked);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesATransientResponseBeforeClassifyingIt()
    {
        const string flaky = "https://site.test/flaky";
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo("/flaky"));
        site.BeforeRespondAsync = url =>
        {
            if (url == flaky)
            {
                if (site.Requested.Count(requested => requested == flaky) == 1)
                {
                    site.Status(flaky, 503);
                }
                else
                {
                    site.Page(flaky, CrawlTestHarness.LinkTo());
                }
            }

            return Task.CompletedTask;
        };

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Count(url => url == flaky).Should().Be(2);
        Classification(sink, flaky).Should().Be(CrawlLinkClassifications.Healthy);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesAPersistentTransientResponseUnknown()
    {
        const string unavailable = "https://site.test/unavailable";
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/unavailable"))
            .With(unavailable, new(503, RetryAfter: TimeSpan.FromSeconds(30)));

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Count(url => url == unavailable).Should().Be(2);
        Classification(sink, unavailable).Should().Be(CrawlLinkClassifications.Unknown);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAtThePageLimitAndSaysSo()
    {
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo("/a", "/b", "/c", "/d"));
        foreach (var path in new[] { "a", "b", "c", "d" })
        {
            site.Page($"https://site.test/{path}", CrawlTestHarness.LinkTo());
        }

        var request = CrawlTestHarness.Request() with { Limits = new() { MaxPages = 3 } };

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, request);

        outcome.StopReason.Should().Be(CrawlStopReasons.PageLimit,
            "a run that hit a budget has not covered the site and must not claim it did");
        outcome.PagesFetched.Should().Be(3);
        sink.Links.Should().Contain(link => link.SkipReason == CrawlSkipReasons.PageLimit);
    }

    [Fact]
    public async Task ExecuteAsync_ChecksButDoesNotFollowBeyondTheDepthLimit()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/one"))
            .Page("https://site.test/one", CrawlTestHarness.LinkTo("/two"))
            .Page("https://site.test/two", CrawlTestHarness.LinkTo("/three"))
            .Page("https://site.test/three", CrawlTestHarness.LinkTo("/four"));

        var request = CrawlTestHarness.Request() with { Limits = new() { MaxDepth = 2 } };

        var (_, sink) = await CrawlTestHarness.RunAsync(site, request);

        site.Requested.Should().Contain("https://site.test/two");
        site.Requested.Should().NotContain("https://site.test/four",
            "a page past the depth limit contributes none of its own links");
        Classification(sink, "https://site.test/two").Should().Be(CrawlLinkClassifications.Healthy);
    }

    [Fact]
    public async Task ExecuteAsync_ObeysRobotsWithoutAnOverride()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/private/secret", "/public/page"))
            .Page("https://site.test/public/page", CrawlTestHarness.LinkTo());
        var robots = new FakeRobotsReader(new(true, "User-agent: *\nDisallow: /private", false));

        var (outcome, sink) = await CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request(), robotsReader: robots);

        site.Requested.Should().NotContain("https://site.test/private/secret");
        sink.Links.Should().ContainSingle(link => link.TargetUrl == "https://site.test/private/secret")
            .Which.SkipReason.Should().Be(CrawlSkipReasons.RobotsDisallowed);
        outcome.RobotsOverrideGranted.Should().BeFalse();
        outcome.RobotsOverrideRefusedBecause.Should().Be(CrawlOverrideRefusals.NotRequested);
    }

    [Fact]
    public async Task ExecuteAsync_GrantsAnOverrideWheneverTheRunAsksForOne()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/private/secret"))
            .Page("https://site.test/private/secret", CrawlTestHarness.LinkTo());
        var robots = new FakeRobotsReader(new(true, "User-agent: *\nDisallow: /private", false));
        var request = CrawlTestHarness.Request() with { RequestRobotsOverride = true };

        var (outcome, _) = await CrawlTestHarness.RunAsync(site, request, robotsReader: robots);

        outcome.RobotsOverrideGranted.Should().BeTrue(
            "an approved exception is no longer required");
        outcome.RobotsOverrideRefusedBecause.Should().BeNull();
        site.Requested.Should().Contain("https://site.test/private/secret");
    }

    [Fact]
    public async Task ExecuteAsync_GrantsAnOverrideOnAProductionTargetToo()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/private/secret"))
            .Page("https://site.test/private/secret", CrawlTestHarness.LinkTo());
        var robots = new FakeRobotsReader(new(true, "User-agent: *\nDisallow: /private", false));
        var request = CrawlTestHarness.Request() with { RequestRobotsOverride = true, IsProduction = true };

        var (outcome, _) = await CrawlTestHarness.RunAsync(site, request, robotsReader: robots);

        outcome.RobotsOverrideGranted.Should().BeTrue();
        site.Requested.Should().Contain("https://site.test/private/secret",
            "the production block was removed by the owner's decision of 2026-08-24");
    }

    [Fact]
    public async Task ExecuteAsync_PreservesFindingsWhenCancelledAndNeverLabelsTheRunComplete()
    {
        using var cancellation = new CancellationTokenSource();
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/a", "/b", "/c"))
            .Page("https://site.test/a", CrawlTestHarness.LinkTo())
            .Status("https://site.test/b", 404)
            .Page("https://site.test/c", CrawlTestHarness.LinkTo());

        site.BeforeRespondAsync = url =>
        {
            if (site.Requested.Count >= 3) cancellation.Cancel();
            return Task.CompletedTask;
        };

        var (outcome, sink) = await CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request(), cancellationToken: cancellation.Token);

        outcome.Status.Should().Be(CrawlRunStatuses.Cancelled);
        outcome.StopReason.Should().Be(CrawlStopReasons.Cancelled);
        outcome.Status.Should().NotBe(CrawlRunStatuses.Completed);
        sink.Links.Should().NotBeEmpty("cancellation preserves what the run already found");
        sink.Links.Should().Contain(link => link.TargetUrl == Seed);

        sink.Links.Select(link => link.TargetUrl).Should().Contain("https://site.test/c");
        sink.Links.Should().OnlyContain(link => link.Classification != CrawlLinkClassifications.Skipped
            || link.SkipReason != null);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnUnusableSeedBeforeMakingARequest()
    {
        var site = Site();

        var (outcome, _) = await CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request("mailto:someone@site.test"));

        outcome.Status.Should().Be(CrawlRunStatuses.Failed);
        outcome.ValidationErrors.Should().NotBeEmpty();
        site.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ConsolidatesTrackingParameterVariationsOntoOneRequest()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo(
                "/target?utm_source=a", "/target?utm_source=b", "/target#top", "/target"))
            .Page("https://site.test/target", CrawlTestHarness.LinkTo());

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Count(url => url.StartsWith("https://site.test/target", StringComparison.Ordinal))
            .Should().Be(1, "BR-L03 and BR-L04: four hrefs, one page");
        sink.Links.Count(link => link.TargetUrl == "https://site.test/target").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesRelativeLinksAgainstTheFirstBaseHref()
    {
        var html = "<!doctype html><html><head><base href=\"/assets/\"><base href=\"/ignored/\"></head>"
            + "<body><a href=\"page\">x</a></body></html>";
        var site = Site()
            .Page(Seed, html)
            .Page("https://site.test/assets/page", CrawlTestHarness.LinkTo());

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Should().Contain("https://site.test/assets/page");
        site.Requested.Should().NotContain("https://site.test/ignored/page");
        sink.Links.Should().ContainSingle(link =>
            link.SourceUrl == Seed && link.TargetUrl == "https://site.test/assets/page");
    }

    [Fact]
    public async Task ExecuteAsync_RedactsSensitiveQueryValuesFromRecordedUrls()
    {
        const string target = "https://site.test/account?access_token=secret&id=7";
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo(target))
            .Status(target, 404);

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Should().Contain(target);
        sink.Links.Should().ContainSingle(link =>
            link.TargetUrl == "https://site.test/account?access_token=REDACTED&id=7"
            && link.TargetUrlIdentity == target);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsARobotsDisallowedRedirectBeforeFetchingItsDestination()
    {
        const string target = "https://site.test/private/landing";
        var site = Site().With(
            Seed,
            new(200, CrawlTestHarness.LinkTo("/should-not-be-read"), RedirectCount: 1, FinalUrl: target));
        var robots = new FakeRobotsReader(new(true, "User-agent: *\nDisallow: /private", false));

        var (outcome, sink) = await CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request(), robotsReader: robots);

        outcome.PagesFetched.Should().Be(0);
        outcome.CoverageLimited.Should().BeTrue();
        sink.Links.Should().ContainSingle(link => link.TargetUrl == Seed)
            .Which.Classification.Should().Be(CrawlLinkClassifications.Blocked);
        site.Requested.Should().NotContain("https://site.test/should-not-be-read");
    }

    [Fact]
    public async Task ExecuteAsync_AppliesTheOverrideToEveryOriginInScope()
    {
        var site = new FakeSiteTransport()
            .Page("https://approved.test/", CrawlTestHarness.LinkTo("/private/x"))
            .Page("https://approved.test/private/x", CrawlTestHarness.LinkTo())
            .Page("https://unapproved.test/", CrawlTestHarness.LinkTo("/private/x"))
            .Page("https://unapproved.test/private/x", CrawlTestHarness.LinkTo());
        var robots = new PerOriginRobotsReader(new()
        {
            ["https://approved.test"] = new(true, Blocking, true),
            ["https://unapproved.test"] = new(true, Blocking, false)
        });
        var request = CrawlTestHarness.Request("https://approved.test/", "https://unapproved.test/") with
        {
            RequestRobotsOverride = true
        };

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, request, robotsReader: robots);

        site.Requested.Should().Contain("https://approved.test/private/x");
        site.Requested.Should().Contain("https://unapproved.test/private/x",
            "no origin has to carry an approval any more");
        sink.Links.Should().NotContain(link => link.SkipReason == CrawlSkipReasons.RobotsDisallowed,
            "nothing is skipped for robots once the override is granted");

        outcome.RobotsOverrideGranted.Should().BeTrue(
            "the run bypassed published restrictions, which is the fact worth recording");
    }

    [Fact]
    public async Task ExecuteAsync_LeavesNoQueuedWorkBehindWhenWorkersRunInParallel()
    {
        var options = CrawlTestHarness.Options with { RequestConcurrency = 4 };
        var site = new FakeSiteTransport().Page(Seed, CrawlTestHarness.LinkTo(
            [.. Enumerable.Range(0, 30).Select(index => $"/p{index}")]));
        foreach (var index in Enumerable.Range(0, 30))
        {
            site.Page($"https://site.test/p{index}",
                CrawlTestHarness.LinkTo($"/p{index}/child"));
            site.Page($"https://site.test/p{index}/child", CrawlTestHarness.LinkTo());
        }

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request(), options);

        outcome.StopReason.Should().Be(CrawlStopReasons.FrontierExhausted);
        outcome.PagesFetched.Should().Be(61, "the seed, thirty pages and thirty children");
        sink.Links.Should().NotContain(link => link.SkipReason == CrawlSkipReasons.RunStopped,
            "a worker must not exit while queued work remains");
    }

    [Fact]
    public async Task ExecuteAsync_StopsGracefullyAndKeepsItsFindingsWhenAFetchThrows()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/a", "/b"))
            .Page("https://site.test/a", CrawlTestHarness.LinkTo())
            .Page("https://site.test/b", CrawlTestHarness.LinkTo());

        site.BeforeRespondAsync = url => url.EndsWith("/a", StringComparison.Ordinal)
            ? throw new InvalidOperationException("transport fault")
            : Task.CompletedTask;

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        outcome.Status.Should().Be(CrawlRunStatuses.Failed);
        outcome.StopReason.Should().Be(CrawlStopReasons.Failed);
        sink.Links.Should().Contain(link => link.TargetUrl == Seed,
            "a run that threw its way out would look like a run that never started");
    }

    [Fact]
    public async Task ExecuteAsync_OverridesRobotsOnADiscoveredOriginToo()
    {
        var site = new FakeSiteTransport()
            .Page("https://approved.test/", CrawlTestHarness.LinkTo(
                "/private/seed-side", "https://other.test/private/discovered"))
            .Page("https://approved.test/private/seed-side", CrawlTestHarness.LinkTo())
            .Page("https://other.test/private/discovered", CrawlTestHarness.LinkTo());
        var robots = new PerOriginRobotsReader(new()
        {
            ["https://approved.test"] = new(true, Blocking, true),
            ["https://other.test"] = new(true, Blocking, false)
        });

        var request = CrawlTestHarness.Request("https://approved.test/") with
        {
            RequestRobotsOverride = true,
            AllowedHosts = [new("approved.test"), new("other.test")],
            AllowedPathPrefixes = []
        };

        var (_, sink) = await CrawlTestHarness.RunAsync(site, request, robotsReader: robots);

        site.Requested.Should().Contain("https://approved.test/private/seed-side");
        site.Requested.Should().Contain("https://other.test/private/discovered",
            "a host inside the run's scope is crawled whatever its robots.txt says");
        sink.Links.Should().NotContain(link => link.SkipReason == CrawlSkipReasons.RobotsDisallowed);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotFollowLinksOutOfAnErrorPage()
    {
        var site = Site()
            .Page(Seed, CrawlTestHarness.LinkTo("/missing"))
            .With("https://site.test/missing", new(404, CrawlTestHarness.LinkTo("/from-error-page")))
            .Page("https://site.test/from-error-page", CrawlTestHarness.LinkTo());

        var (outcome, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        site.Requested.Should().NotContain("https://site.test/from-error-page",
            "an error page's navigation must not expand the crawl");
        outcome.PagesFetched.Should().Be(1, "a 404 is not a page that was fetched");
        Classification(sink, "https://site.test/missing").Should().Be(CrawlLinkClassifications.Broken);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsAnHrefThatCouldNotBeCanonicalised()
    {
        var overlong = $"https://site.test/{new string('a', CrawlUrlOptions.MaxUrlLength)}";
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo(
            overlong, "mailto:someone@site.test", "tel:+15550100"));

        var (_, sink) = await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request());

        sink.Links.Should().ContainSingle(link => link.SkipReason == CrawlUrlRejections.TooLong,
            "a link the crawler refused to resolve is a decision, not an absence");
        sink.Links.Should().NotContain(link => link.TargetUrl.StartsWith("mailto:", StringComparison.Ordinal),
            "mailto and tel are ordinary page content, not defects worth reporting");
        sink.Links.Should().NotContain(link => link.TargetUrl.StartsWith("tel:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsAPageLimitedInternalLinkAsInternal()
    {
        var site = Site().Page(Seed, CrawlTestHarness.LinkTo("/a", "/b"));
        site.Page("https://site.test/a", CrawlTestHarness.LinkTo());
        site.Page("https://site.test/b", CrawlTestHarness.LinkTo());

        var request = CrawlTestHarness.Request() with { Limits = new() { MaxPages = 2 } };

        var (_, sink) = await CrawlTestHarness.RunAsync(site, request);

        var refused = sink.Links.Should()
            .ContainSingle(link => link.SkipReason == CrawlSkipReasons.PageLimit).Subject;
        refused.IsInternal.Should().BeTrue("the link is internal whether or not the frontier took it");
        refused.Depth.Should().Be(1, "its depth is known even though it was never queued");
    }

    private static string? Classification(RecordingCrawlResultSink sink, string target) =>
        sink.Links.SingleOrDefault(link => link.TargetUrl == target)?.Classification;
}

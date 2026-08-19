using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-L03 and BR-L05. These are the rules that decide whether a crawl terminates, so they are
/// driven all the way to their limits here rather than against a live site.
/// </summary>
public sealed class CrawlFrontierTests
{
    private static CrawlUrl Url(string value) =>
        CrawlUrlNormalizer.Normalize(value, CrawlUrlOptions.Default).Url!;

    private static CrawlFrontier Frontier(CrawlLimits? limits = null, params string[] seeds) =>
        new(CrawlScope.FromSeeds([.. (seeds.Length == 0 ? ["https://example.com/"] : seeds).Select(Url)]),
            limits ?? CrawlLimits.Default);

    [Fact]
    public void Constructor_QueuesEverySeedAtDepthZero()
    {
        var frontier = Frontier(null, "https://example.com/", "https://example.com/second");

        var items = Drain(frontier);

        items.Should().HaveCount(2);
        items.Should().OnlyContain(item => item.Depth == 0 && item.Mode == CrawlVisitMode.Follow);
    }

    [Fact]
    public void Offer_AdmitsAUrlOnlyOnce()
    {
        var frontier = Frontier();

        frontier.Offer(Url("https://example.com/a"), 1).Admitted.Should().BeTrue();
        var second = frontier.Offer(Url("https://example.com/a"), 1);

        second.Admitted.Should().BeFalse();
        second.SkipReason.Should().Be(CrawlSkipReasons.AlreadySeen);
    }

    [Fact]
    public void Offer_TreatsAnchorVariationsOfOnePageAsOneTarget()
    {
        var frontier = Frontier();
        var options = CrawlUrlOptions.Default;
        var page = CrawlUrlNormalizer.Normalize("https://example.com/p", options).Url!;
        var anchored = CrawlUrlNormalizer.Normalize("https://example.com/p#section", options).Url!;

        frontier.Offer(page, 1).Admitted.Should().BeTrue();

        frontier.Offer(anchored, 1).SkipReason.Should().Be(CrawlSkipReasons.AlreadySeen);
    }

    [Fact]
    public void Offer_KeepsTheShallowestDepthWhenAPageIsRediscoveredDeeper()
    {
        var frontier = Frontier(new() { MaxDepth = 2 });

        frontier.Offer(Url("https://example.com/shared"), 1).Mode.Should().Be(CrawlVisitMode.Follow);
        frontier.Offer(Url("https://example.com/shared"), 9).SkipReason.Should().Be(CrawlSkipReasons.AlreadySeen);

        Drain(frontier).Should().ContainSingle(item => item.Url.Value.EndsWith("/shared"))
            .Which.Depth.Should().Be(1, "a deep rediscovery must not push a shallow page past the limit");
    }

    [Fact]
    public void Offer_ChecksButDoesNotFollowAnInternalPagePastTheDepthLimit()
    {
        var frontier = Frontier(new() { MaxDepth = 2 });

        frontier.Offer(Url("https://example.com/at-limit"), 2).Mode.Should().Be(CrawlVisitMode.Follow);
        var beyond = frontier.Offer(Url("https://example.com/beyond"), 3);

        beyond.Admitted.Should().BeTrue("a broken link at depth six is still a broken link");
        beyond.Mode.Should().Be(CrawlVisitMode.CheckOnly);
    }

    [Fact]
    public void Offer_ChecksAnExternalLinkWithoutFollowingIt()
    {
        var frontier = Frontier();

        var admission = frontier.Offer(Url("https://elsewhere.test/x"), 1);

        admission.Admitted.Should().BeTrue();
        admission.Mode.Should().Be(CrawlVisitMode.CheckOnly, "BR-L08: external targets are not explored");
    }

    [Fact]
    public void Offer_StopsAdmittingPagesAtThePageLimit()
    {
        var frontier = Frontier(new() { MaxPages = 3 });

        frontier.Offer(Url("https://example.com/a"), 1).Admitted.Should().BeTrue();
        frontier.Offer(Url("https://example.com/b"), 1).Admitted.Should().BeTrue();

        var refused = frontier.Offer(Url("https://example.com/c"), 1);

        refused.SkipReason.Should().Be(CrawlSkipReasons.PageLimit, "the seed spent the first of three");
        frontier.PageBudgetExhausted.Should().BeTrue();
        frontier.PagesAdmitted.Should().Be(3);
    }

    [Fact]
    public void Offer_StillChecksExternalLinksAfterThePageBudgetIsSpent()
    {
        var frontier = Frontier(new() { MaxPages = 1 });

        frontier.Offer(Url("https://example.com/a"), 1).SkipReason.Should().Be(CrawlSkipReasons.PageLimit);
        frontier.Offer(Url("https://elsewhere.test/x"), 1).Admitted.Should().BeTrue();
    }

    [Fact]
    public void Offer_BoundsStatusOnlyRequestsSeparatelyFromPages()
    {
        var frontier = Frontier(new() { MaxCheckOnlyRequests = 2 });

        frontier.Offer(Url("https://elsewhere.test/1"), 1).Admitted.Should().BeTrue();
        frontier.Offer(Url("https://elsewhere.test/2"), 1).Admitted.Should().BeTrue();

        frontier.Offer(Url("https://elsewhere.test/3"), 1).SkipReason
            .Should().Be(CrawlSkipReasons.ExternalCheckLimit,
                "one page with ten thousand outbound links must not make ten thousand requests");
    }

    [Fact]
    public void Offer_CapsQueryVariantsPerPath()
    {
        var frontier = Frontier(new() { MaxQueryVariantsPerPath = 3 });

        for (var index = 0; index < 3; index++)
        {
            frontier.Offer(Url($"https://example.com/facets?colour={index}"), 1)
                .Admitted.Should().BeTrue();
        }

        frontier.Offer(Url("https://example.com/facets?colour=4"), 1).SkipReason
            .Should().Be(CrawlSkipReasons.QueryVariantCap);
    }

    [Fact]
    public void Offer_CountsQueryVariantsPerPathRatherThanPerRun()
    {
        var frontier = Frontier(new() { MaxQueryVariantsPerPath = 1 });

        frontier.Offer(Url("https://example.com/a?x=1"), 1).Admitted.Should().BeTrue();
        frontier.Offer(Url("https://example.com/a?x=2"), 1).SkipReason
            .Should().Be(CrawlSkipReasons.QueryVariantCap);

        frontier.Offer(Url("https://example.com/b?x=1"), 1).Admitted.Should()
            .BeTrue("one exploding section must not consume another section's budget");
    }

    [Fact]
    public void Offer_NeverCapsThePathsOwnQuerylessForm()
    {
        var frontier = Frontier(new() { MaxQueryVariantsPerPath = 1 });

        frontier.Offer(Url("https://example.com/facets?a=1"), 1).Admitted.Should().BeTrue();

        frontier.Offer(Url("https://example.com/facets"), 1).Admitted.Should()
            .BeTrue("the page the section is named by is not a variant of itself");
    }

    [Fact]
    public void Offer_SeparatesVariantCountsAcrossOrigins()
    {
        var scope = new CrawlScope(
            [Url("https://a.test/")],
            [new("a.test"), new("b.test")],
            []);
        var frontier = new CrawlFrontier(scope, new() { MaxQueryVariantsPerPath = 1 });

        frontier.Offer(Url("https://a.test/p?x=1"), 1).Admitted.Should().BeTrue();
        frontier.Offer(Url("https://b.test/p?x=1"), 1).Admitted.Should().BeTrue();
    }

    [Fact]
    public void Dequeue_DrainsBreadthFirst()
    {
        var frontier = Frontier();
        frontier.Offer(Url("https://example.com/depth-1"), 1);
        frontier.Offer(Url("https://example.com/depth-2"), 2);

        Drain(frontier).Select(item => item.Depth).Should().ContainInOrder(0, 1, 2);
    }

    [Fact]
    public void Frontier_TerminatesOnASiteWhereEveryPageLinksToEveryOther()
    {
        var frontier = Frontier(new() { MaxPages = 50, MaxDepth = 10 });
        var pages = Enumerable.Range(0, 20).Select(index => $"https://example.com/p{index}").ToArray();
        var fetched = 0;

        while (frontier.TryDequeue(out var item))
        {
            fetched++;
            fetched.Should().BeLessThan(200, "a fully connected site must not loop");
            if (item.Mode != CrawlVisitMode.Follow) continue;

            foreach (var page in pages)
            {
                frontier.Offer(Url($"{page}#{item.Url.Path}"), item.Depth + 1);
            }
        }

        fetched.Should().Be(21, "the seed plus twenty distinct pages, each visited once");
    }

    private static List<CrawlWorkItem> Drain(CrawlFrontier frontier)
    {
        var items = new List<CrawlWorkItem>();
        while (frontier.TryDequeue(out var item)) items.Add(item);
        return items;
    }
}

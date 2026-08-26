using FluentAssertions;
using WebHealth.Application.Crawling;
using WebHealth.Application.PngAudits;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.PngAudits;
using WebHealth.Infrastructure.SiteAnalysis;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngSiteCrawlerTests
{
    private const string Seed = "https://site.test/app/";

    [Fact]
    public async Task DiscoverAsync_ProducesPagesImagesMappingsAndSafeSkipsWithoutFetchingImages()
    {
        var transport = new FakeSiteTransport()
            .Page(Seed,
                "<html><head><base href=\"/assets/\"></head><body>"
                + "<a href=\"/app/next\">next</a><a href=\"https://outside.test/page\">outside</a>"
                + "<img src=\"logo.png?b=2&amp;token=secret&amp;a=%2f#preview\" "
                + "srcset=\"logo.png?b=2&amp;token=secret&amp;a=%2f 1x, logo.png?w=800 2x\">"
                + "<picture><source srcset=\"//cdn.site.test/wide.png 640w\"></picture>"
                + "<img src=\"data:image/png;base64,AAAA\">"
                + "<img src=\"https://outside.test/out.png?token=secret\">"
                + "</body></html>")
            .Page("https://site.test/app/next",
                "<html><body><img src=\"/assets/logo.png?b=2&amp;token=secret&amp;a=%2f\">"
                + "<img src=\"/other/extensionless\"></body></html>");

        var result = await DiscoverAsync(transport);

        result.Pages.Should().HaveCount(2);
        result.Images.Should().HaveCount(4);
        result.SourceMappings.Should().HaveCount(6);
        result.Skips.Select(skip => skip.Reason).Should().BeEquivalentTo(
            [PngDiscoverySkipReason.DataUrl, PngDiscoverySkipReason.ExternalAssetHost]);
        result.Skips.Single(skip => skip.Reason == PngDiscoverySkipReason.ExternalAssetHost)
            .SafeRawValue.Should().Contain("token=REDACTED");
        result.Images.Should().Contain(image =>
            image.FetchUrl == "https://site.test/assets/logo.png?b=2&token=secret&a=%2f"
            && image.DisplayUrl == "https://site.test/assets/logo.png?b=2&token=REDACTED&a=%2f");
        result.Images.Should().Contain(image => image.FetchUrl == "https://site.test/other/extensionless");
        transport.Requested.Should().Equal(Seed, "https://site.test/app/next");
    }

    [Fact]
    public async Task DiscoverAsync_UsesTheRedirectedDocumentAsTheResolutionAndSourceIdentity()
    {
        const string redirected = "https://site.test/app/folder/home";
        var transport = new FakeSiteTransport().With(
            Seed,
            new(200,
                "<html><body><a href=\"next\">next</a><img src=\"../../assets/photo.png\"></body></html>",
                RedirectCount: 1,
                FinalUrl: redirected))
            .Page("https://site.test/app/folder/next", "<html></html>");

        var result = await DiscoverAsync(transport);

        result.Pages.Select(page => page.DisplayUrl).Should().Equal(
            redirected,
            "https://site.test/app/folder/next");
        result.Images.Should().ContainSingle()
            .Which.FetchUrl.Should().Be("https://site.test/assets/photo.png");
        result.SourceMappings.Should().ContainSingle()
            .Which.SourcePageDisplayUrl.Should().Be(redirected);
    }

    [Fact]
    public async Task DiscoverAsync_KeepsQueryBearingPagesDistinctFromDisplayOnlyDestinations()
    {
        const string firstPage = "https://site.test/app/page?a=1";
        const string secondPage = "https://site.test/app/page?a=2";
        var transport = new FakeSiteTransport()
            .Page(Seed,
                $"<html><body><a href=\"{firstPage}\">one</a>"
                + $"<a href=\"{secondPage}\">two</a></body></html>")
            .With(firstPage, new(
                200,
                "<html><body><img src=\"/one.png\"></body></html>",
                DisplayUrl: "https://site.test/app/page"))
            .With(secondPage, new(
                200,
                "<html><body><img src=\"/two.png\"></body></html>",
                DisplayUrl: "https://site.test/app/page"));

        var result = await DiscoverAsync(transport);

        result.Pages.Select(page => page.DisplayUrl).Should().Contain([
            firstPage,
            secondPage
        ]);
        result.Images.Select(image => image.FetchUrl).Should().Contain([
            "https://site.test/one.png",
            "https://site.test/two.png"
        ]);
    }

    [Fact]
    public async Task DiscoverAsync_EnforcesImageReferenceUniqueImageAndSourceMappingLimits()
    {
        var transport = new FakeSiteTransport().Page(
            Seed,
            "<html><body>"
            + "<img src=\"/one\"><img srcset=\"/one 2x\">"
            + "<img src=\"/two\"><img src=\"/three\"><img src=\"/four\">"
            + "</body></html>");
        var profile = Profile(
            maxImageReferencesPerPage: 4,
            maxUniqueImages: 2,
            maxSourceMappings: 2);

        var result = await DiscoverAsync(transport, profile);

        result.Images.Should().HaveCount(2);
        result.SourceMappings.Should().HaveCount(2);
        result.Skips.Select(skip => skip.Reason).Should().BeEquivalentTo(
            [
                PngDiscoverySkipReason.ReferenceLimit,
                PngDiscoverySkipReason.SourceMappingLimit,
                PngDiscoverySkipReason.UniqueImageLimit
            ]);
        result.CoverageReasons.Should().Contain(reason =>
            reason.Area == PngCoverageArea.ImageAnalysis
            && reason.Reason == PngCoverageReasonCode.ImageReferenceLimit);
        result.CoverageReasons.Should().Contain(reason =>
            reason.Area == PngCoverageArea.ImageAnalysis
            && reason.Reason == PngCoverageReasonCode.UniqueImageLimit);
        result.CoverageReasons.Should().Contain(reason =>
            reason.Area == PngCoverageArea.SourceMappings
            && reason.Reason == PngCoverageReasonCode.SourceMappingLimit);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsDocumentFailureWithoutReferenceLimitReasons()
    {
        var transport = new FakeSiteTransport().Page(Seed, "<html></html>");

        var result = await DiscoverAsync(
            transport,
            discoveryExtractor: new NotInspectedExtractor());

        result.CoverageReasons.Should().HaveCount(2);
        result.CoverageReasons.Should().OnlyContain(reason =>
            reason.Reason == PngCoverageReasonCode.DocumentNotInspected);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsPageDepthAndRobotsCoverageLimits()
    {
        var transport = new FakeSiteTransport()
            .Page(Seed,
                "<html><body><a href=\"/app/one\">one</a>"
                + "<a href=\"/app/two\">two</a><a href=\"/private\">private</a></body></html>")
            .Page("https://site.test/app/one",
                "<html><body><a href=\"/app/deep\">deep</a></body></html>");
        var profile = Profile(maxPages: 2, maxDepth: 1);
        var robots = new FakeRobotsReader(new(
            true,
            "User-agent: *\nDisallow: /private",
            false));

        var result = await DiscoverAsync(transport, profile, robotsReader: robots);

        result.Pages.Should().HaveCount(2);
        result.CoverageReasons.Should().Contain(reason => reason.Reason == PngCoverageReasonCode.PageLimit);
        result.CoverageReasons.Should().Contain(reason => reason.Reason == PngCoverageReasonCode.DepthLimit);
        transport.Requested.Should().NotContain("https://site.test/private");
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotFetchARobotsDisallowedPage()
    {
        var transport = new FakeSiteTransport()
            .Page(Seed, "<html><body><a href=\"/app/private\">private</a></body></html>")
            .Page("https://site.test/app/private", "<html></html>");
        var robots = new FakeRobotsReader(new(
            true,
            "User-agent: *\nDisallow: /app/private",
            false));

        var result = await DiscoverAsync(transport, robotsReader: robots);

        result.Pages.Should().ContainSingle();
        result.CoverageReasons.Should().ContainSingle(reason =>
            reason.Reason == PngCoverageReasonCode.RobotsDisallowed);
        transport.Requested.Should().Equal(Seed);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsAPageRedirectOutsideThePageScope()
    {
        var transport = new FakeSiteTransport().With(
            Seed,
            new(200,
                "<html><body><img src=\"/image.png\"></body></html>",
                RedirectCount: 1,
                FinalUrl: "https://outside.test/page"));

        var result = await DiscoverAsync(transport);

        result.Pages.Should().BeEmpty();
        result.Images.Should().BeEmpty();
        result.CoverageReasons.Should().ContainSingle(reason =>
            reason.Reason == PngCoverageReasonCode.RedirectOutOfScope);
        transport.Requested.Should().Equal(Seed);
    }

    [Fact]
    public async Task DiscoverAsync_StopsWhenTheTotalPageByteBudgetIsReached()
    {
        var root = "<html><body><a href=\"/app/next\">next</a></body></html>";
        var next = new string('x', 80);
        var transport = new FakeSiteTransport()
            .Page(Seed, root)
            .With("https://site.test/app/next", new(200, next, Truncated: true));
        var profile = Profile(maxPageBytes: 100, maxTotalPageBytes: 120);

        var result = await DiscoverAsync(transport, profile);

        result.TotalPageBytes.Should().BeGreaterThanOrEqualTo(120);
        result.CoverageReasons.Should().ContainSingle(reason =>
            reason.Reason == PngCoverageReasonCode.TotalPageBytesLimit);
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotStartAPageAfterTheHttpAttemptBudgetIsSpent()
    {
        var transport = new FakeSiteTransport()
            .Page(Seed, "<html><body><a href=\"/app/next\">next</a></body></html>")
            .Page("https://site.test/app/next", "<html></html>");
        var profile = Profile(maxTotalHttpAttempts: 1);

        var result = await DiscoverAsync(transport, profile);

        result.HttpAttempts.Should().Be(1);
        result.CoverageReasons.Should().ContainSingle(reason =>
            reason.Reason == PngCoverageReasonCode.HttpAttemptLimit);
        transport.Requested.Should().Equal(Seed);
    }

    [Fact]
    public async Task DiscoverAsync_CountsRedirectExchangesAgainstTheHttpAttemptBudget()
    {
        var transport = new FakeSiteTransport().With(
            Seed,
            new(200, "<html></html>", RedirectCount: 5, FinalUrl: "https://site.test/app/final"));
        var profile = Profile(maxTotalHttpAttempts: 2);

        var result = await DiscoverAsync(transport, profile);

        result.HttpAttempts.Should().Be(2);
        result.Pages.Should().BeEmpty();
        result.CoverageReasons.Should().ContainSingle(reason =>
            reason.Reason == PngCoverageReasonCode.HttpAttemptLimit);
    }

    [Fact]
    public async Task DiscoverAsync_CancelsInFlightWorkAtTheRunDeadline()
    {
        var fetcher = new BlockingFetcher();
        var crawler = Crawler(fetcher);

        var result = await crawler.DiscoverAsync(Request(Profile(
            maxDuration: TimeSpan.FromMilliseconds(50))));

        fetcher.CancellationObserved.Should().BeTrue();
        result.CoverageReasons.Should().ContainSingle(reason =>
            reason.Reason == PngCoverageReasonCode.DurationLimit);
    }

    [Fact]
    public async Task DiscoverAsync_PreservesCallerCancellation()
    {
        var fetcher = new BlockingFetcher();
        var crawler = Crawler(fetcher);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => crawler.DiscoverAsync(Request(Profile()), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task<PngSiteDiscoveryResult> DiscoverAsync(
        FakeSiteTransport transport,
        PngSiteDiscoveryProfile? profile = null,
        IReadOnlyList<CrawlHostRule>? assetHosts = null,
        ICrawlRobotsReader? robotsReader = null,
        IHtmlDocumentDiscoveryExtractor? discoveryExtractor = null)
    {
        var timeProvider = TimeProvider.System;
        var transportOptions = new SafeHttpTransportOptions();
        var fetcher = new SiteAnalysisFetcher(
            transport,
            new SiteAnalysisRequestBudget(transportOptions),
            new SiteAnalysisHostRateLimiter(timeProvider),
            timeProvider);
        var crawler = Crawler(
            fetcher,
            robotsReader,
            transportOptions,
            timeProvider,
            discoveryExtractor);
        return await crawler.DiscoverAsync(Request(profile ?? Profile(), assetHosts));
    }

    private static PngSiteCrawler Crawler(
        ISiteAnalysisFetcher fetcher,
        ICrawlRobotsReader? robotsReader = null,
        SafeHttpTransportOptions? transportOptions = null,
        TimeProvider? timeProvider = null,
        IHtmlDocumentDiscoveryExtractor? discoveryExtractor = null) =>
        new(
            fetcher,
            discoveryExtractor ?? new HtmlDocumentDiscoveryExtractor(),
            robotsReader ?? new FakeRobotsReader(),
            transportOptions ?? new SafeHttpTransportOptions(),
            timeProvider ?? TimeProvider.System);

    private static PngSiteCrawlRequest Request(
        PngSiteDiscoveryProfile profile,
        IReadOnlyList<CrawlHostRule>? assetHosts = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            new(
                Seed,
                [new CrawlHostRule("site.test")],
                ["/app/"],
                assetHosts ??
                [new CrawlHostRule("site.test"), new CrawlHostRule("cdn.site.test")],
                CrawlUrlOptions.Default),
            profile);

    private static PngSiteDiscoveryProfile Profile(
        int maxPages = 20,
        int maxDepth = 5,
        int maxPageBytes = 1024,
        long maxTotalPageBytes = 16 * 1024,
        int maxImageReferencesPerPage = 100,
        int maxUniqueImages = 50,
        int maxSourceMappings = 100,
        int maxTotalHttpAttempts = 100,
        TimeSpan? maxDuration = null) =>
        new(
            new(
                maxPages,
                maxDepth,
                maxPageBytes,
                maxTotalPageBytes,
                maxImageReferencesPerPage),
            new(maxUniqueImages, maxSourceMappings),
            new(maxTotalHttpAttempts, 5, 0, 0, maxDuration ?? TimeSpan.FromMinutes(5)));

    private sealed class BlockingFetcher : ISiteAnalysisFetcher
    {
        public bool CancellationObserved { get; private set; }

        public async Task<SiteAnalysisFetchResult> FetchAsync(
            SiteAnalysisFetchRequest request,
            SiteAnalysisFetchProfile profile,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            }
            finally
            {
                CancellationObserved = cancellationToken.IsCancellationRequested;
            }
        }
    }

    private sealed class NotInspectedExtractor : IHtmlDocumentDiscoveryExtractor
    {
        public HtmlDocumentDiscovery Extract(ReadOnlyMemory<byte> body, string? contentType) =>
            HtmlDocumentDiscovery.NotInspected;
    }
}

using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-L03 and BR-L04, as written down in docs/phase-6/Crawl_Scope_And_URL_Identity.md. The
/// canonical crawl URL is the revisit key, so each of these is a statement about whether the
/// crawler terminates or whether it silently misses pages.
/// </summary>
public sealed class CrawlUrlNormalizerTests
{
    private static string? Canonical(string url, CrawlUrlOptions? options = null) =>
        CrawlUrlNormalizer.Normalize(url, options ?? CrawlUrlOptions.Default).Url?.Value;

    private static string? Rejection(string url, CrawlUrlOptions? options = null) =>
        CrawlUrlNormalizer.Normalize(url, options ?? CrawlUrlOptions.Default).Rejection;

    private static CrawlUrl Base(string url) =>
        CrawlUrlNormalizer.Normalize(url, CrawlUrlOptions.Default).Url!;

    [Theory]
    [InlineData("HTTP://Example.COM./a", "http://example.com/a")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/a/../b", "https://example.com/b")]
    [InlineData("https://example.com/%7Euser", "https://example.com/~user")]
    [InlineData("https://example.com/a%2fb", "https://example.com/a%2Fb")]
    public void Normalize_AppliesTheSharedIdentityRules(string input, string expected) =>
        Canonical(input).Should().Be(expected);

    [Fact]
    public void Normalize_KeepsANonDefaultPort()
    {
        Canonical("https://example.com:8443/a").Should().Be("https://example.com:8443/a");
        Canonical("https://example.com:8443/a").Should().NotBe(Canonical("https://example.com/a"));
    }

    [Fact]
    public void Normalize_RemovesTheFragment()
    {
        Canonical("https://example.com/page#top").Should().Be("https://example.com/page");
        Canonical("https://example.com/page#section-2")
            .Should().Be(Canonical("https://example.com/page"),
                "an anchor is a position in a document, not a document (BR-L03)");
    }

    [Fact]
    public void Normalize_PreservesPathCaseAndTheTrailingSlash()
    {
        Canonical("https://example.com/About").Should().NotBe(Canonical("https://example.com/about"));
        Canonical("https://example.com/docs").Should().NotBe(Canonical("https://example.com/docs/"));
        Canonical("https://example.com/").Should().NotBe(Canonical("https://example.com/index.html"));
    }

    [Theory]
    [InlineData("mailto:someone@example.com", CrawlUrlRejections.UnsupportedScheme)]
    [InlineData("tel:+15550100", CrawlUrlRejections.UnsupportedScheme)]
    [InlineData("javascript:alert(1)", CrawlUrlRejections.UnsupportedScheme)]
    [InlineData("data:text/html,<p>x", CrawlUrlRejections.UnsupportedScheme)]
    [InlineData("ftp://example.com/a", CrawlUrlRejections.UnsupportedScheme)]
    [InlineData("https://user:secret@example.com/a", CrawlUrlRejections.CredentialsPresent)]
    [InlineData("not a url", CrawlUrlRejections.Malformed)]
    [InlineData("", CrawlUrlRejections.Malformed)]
    public void Normalize_RefusesWhatIsNotACrawlTarget(string input, string expected)
    {
        CrawlUrlNormalizer.Normalize(input, CrawlUrlOptions.Default).Succeeded.Should().BeFalse();
        Rejection(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_RejectsAUrlLongerThanTheStoredBound()
    {
        var url = $"https://example.com/{new string('a', CrawlUrlOptions.MaxUrlLength)}";

        Rejection(url).Should().Be(CrawlUrlRejections.TooLong);
    }

    [Fact]
    public void Normalize_DropsTrackingParametersByDefault()
    {
        Canonical("https://example.com/p?utm_source=x&utm_medium=y&id=7")
            .Should().Be("https://example.com/p?id=7");
        Canonical("https://example.com/p?gclid=abc").Should().Be("https://example.com/p");
        Canonical("https://example.com/p?UTM_Source=x")
            .Should().Be("https://example.com/p", "parameter names are matched case-insensitively");
    }

    [Fact]
    public void Normalize_ConsolidatesUtmVariationsOntoOneTarget()
    {
        var first = Canonical("https://example.com/p?utm_source=newsletter");
        var second = Canonical("https://example.com/p?utm_source=twitter&utm_campaign=spring");

        first.Should().Be(second).And.Be("https://example.com/p");
    }

    [Fact]
    public void Normalize_SortsTheSurvivingQueryUnderTheDefaultPolicy()
    {
        Canonical("https://example.com/p?b=2&a=1").Should().Be("https://example.com/p?a=1&b=2");
        Canonical("https://example.com/p?b=2&a=1").Should().Be(Canonical("https://example.com/p?a=1&b=2"));
    }

    [Fact]
    public void Normalize_KeepsTheAuthoredOrderUnderThePreserveOrderPolicy()
    {
        var options = CrawlUrlOptions.Default with { QueryPolicy = CrawlQueryPolicy.PreserveOrder };

        Canonical("https://example.com/p?b=2&a=1&utm_source=x", options)
            .Should().Be("https://example.com/p?b=2&a=1");
    }

    [Fact]
    public void Normalize_DropsTheWholeQueryUnderTheIgnorePolicy()
    {
        var options = CrawlUrlOptions.Default with { QueryPolicy = CrawlQueryPolicy.Ignore };

        Canonical("https://example.com/list?page=2", options).Should().Be("https://example.com/list");
    }

    [Fact]
    public void Normalize_KeepsPaginationDistinctUnderTheDefaultPolicy()
    {
        Canonical("https://example.com/list?page=2")
            .Should().NotBe(Canonical("https://example.com/list?page=3"),
                "collapsing pagination is the 'misses real pages' failure");
    }

    [Fact]
    public void Normalize_RejectsAUrlCarryingMoreParametersThanTheCap()
    {
        var query = string.Join('&', Enumerable.Range(0, CrawlUrlOptions.DefaultMaxQueryParameters + 1)
            .Select(index => $"f{index}={index}"));

        Rejection($"https://example.com/facets?{query}")
            .Should().Be(CrawlUrlRejections.TooManyQueryParameters);
    }

    [Fact]
    public void Normalize_CountsOnlyTheParametersThatSurviveAgainstTheCap()
    {
        var tracking = string.Join('&', Enumerable.Range(0, 20).Select(index => $"utm_{index}=x"));

        Canonical($"https://example.com/p?{tracking}&id=7")
            .Should().Be("https://example.com/p?id=7",
                "a page carrying twenty utm parameters is an ordinary page, not a permutation");
    }

    [Fact]
    public void Normalize_TreatsAnOverriddenTrackingSetAsAuthoritative()
    {
        var options = CrawlUrlOptions.Default with
        {
            TrackingParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sid" }
        };

        Canonical("https://example.com/p?ref=abc&sid=1", options)
            .Should().Be("https://example.com/p?ref=abc",
                "a site that routes on ref must be able to say so");
    }

    [Theory]
    [InlineData("/other", "https://example.com/other")]
    [InlineData("other", "https://example.com/docs/other")]
    [InlineData("../up", "https://example.com/up")]
    [InlineData("//cdn.example.com/asset", "https://cdn.example.com/asset")]
    [InlineData("https://elsewhere.test/x", "https://elsewhere.test/x")]
    [InlineData("#anchor", "https://example.com/docs/page")]
    public void Resolve_ResolvesAgainstThePageTheLinkWasFoundOn(string href, string expected) =>
        CrawlUrlNormalizer.Resolve(href, Base("https://example.com/docs/page"), CrawlUrlOptions.Default)
            .Url!.Value.Should().Be(expected);

    [Fact]
    public void Resolve_StripsControlCharactersFromAWrappedHref()
    {
        var result = CrawlUrlNormalizer.Resolve(
            "\n  /docs/a\t\n  ", Base("https://example.com/"), CrawlUrlOptions.Default);

        result.Url!.Value.Should().Be("https://example.com/docs/a",
            "markup wrapping must not turn one target into several");
    }

    [Fact]
    public void Resolve_RefusesASchemeThatIsNotACrawlTarget() =>
        CrawlUrlNormalizer.Resolve("javascript:void(0)", Base("https://example.com/"), CrawlUrlOptions.Default)
            .Rejection.Should().Be(CrawlUrlRejections.UnsupportedScheme);

    [Theory]
    [InlineData("https://example.com/docs/page", "/docs/")]
    [InlineData("https://example.com/docs/", "/docs/")]
    [InlineData("https://example.com/", "/")]
    [InlineData("https://example.com/page", "/")]
    [InlineData("https://example.com/a/b/c", "/a/b/")]
    public void Directory_IsThePathWithoutItsLastSegment(string url, string expected) =>
        Base(url).Directory.Should().Be(expected);

    [Theory]
    [InlineData("https://example.com/a", "https://example.com")]
    [InlineData("https://example.com:8443/a?b=1", "https://example.com:8443")]
    [InlineData("http://example.com/", "http://example.com")]
    public void Origin_IsSchemeHostAndEffectivePort(string url, string expected) =>
        Base(url).Origin.Should().Be(expected);
}

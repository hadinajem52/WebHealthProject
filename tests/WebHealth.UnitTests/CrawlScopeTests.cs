using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>BR-L01. Where a crawl may go, decided before it makes a single request.</summary>
public sealed class CrawlScopeTests
{
    private static CrawlUrl Url(string value) =>
        CrawlUrlNormalizer.Normalize(value, CrawlUrlOptions.Default).Url!;

    private static CrawlScope ScopeOf(params string[] seeds) =>
        CrawlScope.FromSeeds([.. seeds.Select(Url)]);

    [Fact]
    public void FromSeeds_KeepsACrawlUnderTheSeedDirectory()
    {
        var scope = ScopeOf("https://example.com/docs/index.html");

        scope.Decide(Url("https://example.com/docs/guide")).Should().Be(CrawlScopeDecision.Internal);
        scope.Decide(Url("https://example.com/blog/post")).Should().Be(CrawlScopeDecision.External);
    }

    [Fact]
    public void FromSeeds_OnARootSeedAllowsTheWholeHost()
    {
        var scope = ScopeOf("https://example.com/");

        scope.Decide(Url("https://example.com/anything/deep")).Should().Be(CrawlScopeDecision.Internal);
    }

    [Fact]
    public void Decide_TreatsASubdomainAsExternalUnlessItIsOptedIn()
    {
        var derived = ScopeOf("https://example.com/");

        derived.Decide(Url("https://app.example.com/x")).Should().Be(CrawlScopeDecision.External);

        var explicitScope = derived with { AllowedHosts = [new("example.com", IncludeSubdomains: true)] };

        explicitScope.Decide(Url("https://app.example.com/x")).Should().Be(CrawlScopeDecision.Internal);
    }

    [Fact]
    public void Decide_DoesNotLetASuffixMasqueradeAsASubdomain()
    {
        var scope = ScopeOf("https://example.com/") with
        {
            AllowedHosts = [new("example.com", IncludeSubdomains: true)]
        };

        scope.Decide(Url("https://notexample.com/x")).Should().Be(CrawlScopeDecision.External);
        scope.Decide(Url("https://evil-example.com/x")).Should().Be(CrawlScopeDecision.External);
    }

    [Fact]
    public void Decide_TreatsADifferentSchemeOrPortAsTheSameHost()
    {
        var scope = ScopeOf("https://example.com/");

        scope.Decide(Url("http://example.com/x")).Should().Be(CrawlScopeDecision.Internal,
            "scope is a host and path question; the scheme is recorded on the result");
    }

    [Fact]
    public void Decide_MatchesPathPrefixesCaseSensitively()
    {
        var scope = ScopeOf("https://example.com/Docs/");

        scope.Decide(Url("https://example.com/docs/a")).Should().Be(CrawlScopeDecision.External);
        scope.Decide(Url("https://example.com/Docs/a")).Should().Be(CrawlScopeDecision.Internal);
    }

    [Fact]
    public void Decide_AllowsTheWholeHostWhenNoPrefixIsConfigured()
    {
        var scope = ScopeOf("https://example.com/docs/") with { AllowedPathPrefixes = [] };

        scope.Decide(Url("https://example.com/blog")).Should().Be(CrawlScopeDecision.Internal);
    }

    [Fact]
    public void Validate_AcceptsAScopeDerivedFromItsOwnSeeds() =>
        ScopeOf("https://example.com/docs/").Validate().Should().BeEmpty();

    [Fact]
    public void Validate_RejectsASeedOutsideItsOwnScope()
    {
        var scope = ScopeOf("https://example.com/docs/") with
        {
            Seeds = [Url("https://elsewhere.test/x")]
        };

        scope.Validate().Should().ContainSingle()
            .Which.Should().Contain("https://elsewhere.test/x");
    }

    [Fact]
    public void Validate_RejectsACrawlWithNoSeedsOrNoHosts()
    {
        var empty = new CrawlScope([], [], []);

        empty.Validate().Should().HaveCount(2);
    }
}

using FluentAssertions;
using WebHealth.Domain.Seo;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-E06 and BR-E07 parsing, as written down in docs/phase-6/Robots_And_Sitemap.md. Everything
/// here is a pure function of text and a path.
/// </summary>
public sealed class RobotsTxtParserTests
{
    private const string Agent = "webhealthmonitor/1.0";

    private static bool IsAllowed(string content, string path, string agent = Agent) =>
        RobotsTxtParser.Evaluate(RobotsTxtParser.Parse(content), agent, path).IsAllowed;

    [Fact]
    public void Parse_TreatsAnEmptyFileAsNoRestrictions()
    {
        RobotsTxtParser.Parse(null).IsEmpty.Should().BeTrue();
        RobotsTxtParser.Parse("   \n\n  ").IsEmpty.Should().BeTrue();
        IsAllowed(string.Empty, "/anything").Should().BeTrue();
    }

    [Fact]
    public void Parse_IgnoresAByteOrderMarkOnTheFirstLine()
    {
        var content = "\uFEFFUser-agent: *\nDisallow: /blocked";

        var rules = RobotsTxtParser.RulesFor(RobotsTxtParser.Parse(content), Agent);

        rules.Should().ContainSingle("a BOM must not turn the first User-agent into an unknown directive");
        rules[0].Pattern.Should().Be("/blocked");
    }

    [Fact]
    public void Parse_StripsCommentsWhereverTheyAppear()
    {
        const string content = """
            # a leading comment
            User-agent: *   # trailing comment
            Disallow: /private   # another
            #Disallow: /commented-out
            """;

        var rules = RobotsTxtParser.RulesFor(RobotsTxtParser.Parse(content), Agent);

        rules.Should().ContainSingle();
        rules[0].Pattern.Should().Be("/private");
    }

    [Fact]
    public void Parse_GroupsConsecutiveUserAgentLinesTogether()
    {
        const string content = """
            User-agent: alpha
            User-agent: beta
            Disallow: /shared
            """;

        var file = RobotsTxtParser.Parse(content);

        file.Groups.Should().ContainSingle();
        file.Groups[0].Agents.Should().BeEquivalentTo("alpha", "beta");
        RobotsTxtParser.RulesFor(file, "alpha/2").Should().ContainSingle();
        RobotsTxtParser.RulesFor(file, "beta/9").Should().ContainSingle();
    }

    [Fact]
    public void Parse_StartsANewGroupOnlyAfterARuleLine()
    {
        const string content = """
            User-agent: alpha
            Disallow: /a
            User-agent: beta
            Disallow: /b
            """;

        var file = RobotsTxtParser.Parse(content);

        file.Groups.Should().HaveCount(2);
        RobotsTxtParser.RulesFor(file, "alpha/1").Single().Pattern.Should().Be("/a");
        RobotsTxtParser.RulesFor(file, "beta/1").Single().Pattern.Should().Be("/b");
    }

    [Fact]
    public void Parse_IgnoresRulesBeforeAnyUserAgent()
    {
        const string content = """
            Disallow: /orphan
            User-agent: *
            Disallow: /real
            """;

        var rules = RobotsTxtParser.RulesFor(RobotsTxtParser.Parse(content), Agent);

        rules.Should().ContainSingle();
        rules[0].Pattern.Should().Be("/real", "a rule with no group cannot be attached to a later one");
    }

    [Fact]
    public void Parse_IgnoresUnknownAndMalformedLinesWithoutThrowing()
    {
        const string content = """
            Crawl-delay: 10
            Host: example.test
            this line has no colon
            : empty directive
            User-agent: *
            Disallow: /x
            Some-Future-Directive: whatever
            """;

        var file = RobotsTxtParser.Parse(content);

        file.Groups.Should().ContainSingle();
        RobotsTxtParser.RulesFor(file, Agent).Single().Pattern.Should().Be("/x");
    }

    [Fact]
    public void Parse_ReadsDirectiveNamesCaseInsensitively()
    {
        const string content = """
            USER-AGENT: *
            DISALLOW: /a
            AlLoW: /a/b
            """;

        RobotsTxtParser.RulesFor(RobotsTxtParser.Parse(content), Agent).Should().HaveCount(2);
    }

    [Fact]
    public void Parse_CollectsSitemapDirectivesRegardlessOfGroup()
    {
        const string content = """
            Sitemap: https://example.test/sitemap.xml
            User-agent: *
            Disallow: /a
            Sitemap: https://example.test/news.xml
            """;

        RobotsTxtParser.Parse(content).Sitemaps.Should().Equal(
            "https://example.test/sitemap.xml", "https://example.test/news.xml");
    }

    [Fact]
    public void RulesFor_PrefersTheMostSpecificAgentOverTheWildcard()
    {
        const string content = """
            User-agent: *
            Disallow: /

            User-agent: webhealthmonitor
            Disallow: /private
            """;

        var file = RobotsTxtParser.Parse(content);

        RobotsTxtParser.RulesFor(file, Agent).Single().Pattern.Should().Be("/private");
        RobotsTxtParser.RulesFor(file, "othercrawler/1").Single().Pattern.Should().Be("/");
    }

    [Fact]
    public void RulesFor_MatchesAgentsCaseInsensitively() =>
        RobotsTxtParser.RulesFor(
            RobotsTxtParser.Parse("User-agent: WebHealthMonitor\nDisallow: /a"), "webhealthmonitor/1.0")
            .Should().ContainSingle();

    [Fact]
    public void RulesFor_MergesGroupsThatMatchEqually()
    {
        const string content = """
            User-agent: webhealthmonitor
            Disallow: /a

            User-agent: webhealthmonitor
            Disallow: /b
            """;

        RobotsTxtParser.RulesFor(RobotsTxtParser.Parse(content), Agent).Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_TreatsAnEmptyDisallowAsNoRestriction() =>
        IsAllowed("User-agent: *\nDisallow:", "/anything").Should().BeTrue();

    [Fact]
    public void Evaluate_BlocksTheWholeSiteOnAWildcardDisallow()
    {
        IsAllowed("User-agent: *\nDisallow: /", "/").Should().BeFalse();
        IsAllowed("User-agent: *\nDisallow: /", "/deep/page").Should().BeFalse();
    }

    [Fact]
    public void Evaluate_LetsTheLongestMatchWin()
    {
        const string content = """
            User-agent: *
            Disallow: /
            Allow: /public
            """;

        IsAllowed(content, "/public/page").Should().BeTrue();
        IsAllowed(content, "/private/page").Should().BeFalse();
    }

    [Fact]
    public void Evaluate_LetsAllowWinAnEqualLengthTie()
    {
        const string content = """
            User-agent: *
            Disallow: /page
            Allow: /page
            """;

        IsAllowed(content, "/page").Should().BeTrue(
            "resolving the tie the other way would report a site as unindexable where crawlers crawl it");
    }

    [Fact]
    public void Evaluate_ComparesPathsCaseSensitively()
    {
        const string content = "User-agent: *\nDisallow: /Private";

        IsAllowed(content, "/Private/x").Should().BeFalse();
        IsAllowed(content, "/private/x").Should().BeTrue("URLs are case-sensitive, unlike directives");
    }

    [Fact]
    public void Evaluate_ReportsTheRuleThatDecided()
    {
        var decision = RobotsTxtParser.Evaluate(
            RobotsTxtParser.Parse("User-agent: *\nDisallow: /admin"), Agent, "/admin/users");

        decision.IsAllowed.Should().BeFalse();
        decision.MatchedRule!.Pattern.Should().Be("/admin");
    }

    [Theory]
    [InlineData("/*.pdf", "/files/report.pdf", true)]
    [InlineData("/*.pdf", "/files/report.pdf?x=1", true)]
    [InlineData("/*.pdf$", "/files/report.pdf", true)]
    [InlineData("/*.pdf$", "/files/report.pdf?x=1", false)]
    [InlineData("/private/", "/private/", true)]
    [InlineData("/private/", "/private", false)]
    [InlineData("/a*b", "/axxxb/c", true)]
    [InlineData("/a*b", "/axxxc", false)]
    [InlineData("/", "/anything", true)]
    [InlineData("/exact$", "/exact", true)]
    [InlineData("/exact$", "/exact/more", false)]
    public void Matches_HandlesWildcardsAndEndAnchors(string pattern, string path, bool expected) =>
        RobotsTxtParser.Matches(pattern, path).Should().Be(expected);

    [Fact]
    public void Matches_DoesNotBlowUpOnAPatternDesignedToBacktrack()
    {
        // A pattern like this is exponential for a naive regex translation; the two-pointer
        // matcher is not, which is the reason it exists.
        var pattern = "/" + string.Concat(Enumerable.Repeat("a*", 40)) + "b$";
        var path = "/" + new string('a', 400);

        var matched = RobotsTxtParser.Matches(pattern, path);

        matched.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AppliesWildcardRulesToTheEndpointPath()
    {
        const string content = """
            User-agent: *
            Disallow: /*/private
            """;

        IsAllowed(content, "/en/private/page").Should().BeFalse();
        IsAllowed(content, "/en/public/page").Should().BeTrue();
    }
}

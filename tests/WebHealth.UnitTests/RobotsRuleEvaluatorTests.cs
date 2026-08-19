using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Seo;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-E06, BR-E07 and BR-E08, as written down in docs/phase-6/Robots_And_Sitemap.md.
/// </summary>
public sealed class RobotsRuleEvaluatorTests
{
    private const string Agent = "WebHealthMonitor/1.0";
    private const string BlockedEverything = "User-agent: *\nDisallow: /";

    private static SeoPolicy Policy(bool isProduction = true) =>
        new("example.test", SeoIndexingExpectations.Indexable, true, isProduction);

    private static RobotsSnapshotFacts Facts(
        string status = RobotsSnapshotStatuses.Fetched,
        string? content = null,
        bool hasException = false,
        bool sitemapRequired = false,
        bool sitemapAvailable = false) =>
        new(status, content, hasException, sitemapRequired, sitemapAvailable);

    private static IReadOnlyList<NormalizedFinding> Evaluate(
        RobotsSnapshotFacts? facts, string path = "/status", bool isProduction = true) =>
        RobotsRuleEvaluator.Evaluate(facts, Agent, path, Policy(isProduction));

    [Fact]
    public void Evaluate_ProducesNothingWhenTheOriginHasNoSnapshotYet() =>
        Evaluate(null).Should().BeEmpty(
            "an empty cache is absence of evidence, not a clean bill of health");

    [Fact]
    public void Evaluate_ProducesNothingForAnOriginWithNoRestrictions() =>
        Evaluate(Facts(content: "User-agent: *\nDisallow:")).Should().BeEmpty();

    [Fact]
    public void Evaluate_TreatsA404AsNoRestrictions() =>
        Evaluate(Facts(RobotsSnapshotStatuses.NotFound)).Should().BeEmpty(
            "no robots.txt means nothing is disallowed, which is a valid answer");

    [Fact]
    public void Evaluate_ReportsAnOriginThatCouldNotAnswer()
    {
        var finding = Evaluate(Facts(RobotsSnapshotStatuses.Unavailable)).Single();

        finding.RuleKey.Should().Be(RobotsRules.Unavailable);
        finding.Severity.Should().Be(FindingSeverities.Warning);
        finding.FailureCategory.Should().Be(SeoFailureCategories.Robots);
    }

    [Fact]
    public void Evaluate_RaisesABlockedProductionSiteAsCritical()
    {
        var finding = Evaluate(Facts(content: BlockedEverything)).Single();

        finding.RuleKey.Should().Be(RobotsRules.BlocksSite);
        finding.Severity.Should().Be(FindingSeverities.Critical,
            "a production site telling every crawler to go away is the whole site leaving search");
        finding.ObservedValue.Should().Be("Disallow: /");
    }

    [Fact]
    public void Evaluate_RaisesABlockedNonProductionSiteOnlyAsAWarning() =>
        Evaluate(Facts(content: BlockedEverything), isProduction: false)
            .Single().Severity.Should().Be(FindingSeverities.Warning);

    [Fact]
    public void Evaluate_ReportsABlockedEndpointSeparatelyFromABlockedSite()
    {
        var findings = Evaluate(Facts(content: "User-agent: *\nDisallow: /status"));

        var finding = findings.Single();
        finding.RuleKey.Should().Be(RobotsRules.BlocksEndpoint);
        finding.Severity.Should().Be(FindingSeverities.High, "production, but not the whole site");
    }

    [Fact]
    public void Evaluate_DoesNotReportTheEndpointSeparatelyWhenTheWholeSiteIsBlocked() =>
        Evaluate(Facts(content: BlockedEverything)).Select(finding => finding.RuleKey)
            .Should().ContainSingle().Which.Should().Be(RobotsRules.BlocksSite,
                "the site-wide finding already says everything the endpoint one would");

    [Fact]
    public void Evaluate_RespectsAnAllowThatUnblocksTheEndpoint() =>
        Evaluate(Facts(content: "User-agent: *\nDisallow: /\nAllow: /status")).Select(f => f.RuleKey)
            .Should().ContainSingle().Which.Should().Be(RobotsRules.BlocksSite,
                "the root is still blocked even though this endpoint is not");

    [Fact]
    public void Evaluate_SuppressesBlockingRulesWhenAnExceptionIsApproved() =>
        Evaluate(Facts(content: BlockedEverything, hasException: true)).Should().BeEmpty(
            "BR-E07: an approved exception is a recorded decision, not a silent flag");

    [Fact]
    public void Evaluate_StillReportsAMissingSitemapUnderAnApprovedRobotsException() =>
        Evaluate(Facts(content: BlockedEverything, hasException: true, sitemapRequired: true))
            .Single().RuleKey.Should().Be(RobotsRules.SitemapMissing);

    [Fact]
    public void Evaluate_ReportsAMissingSitemapOnlyWhereOneIsRequired()
    {
        Evaluate(Facts(sitemapRequired: false, sitemapAvailable: false)).Should().BeEmpty();
        Evaluate(Facts(sitemapRequired: true, sitemapAvailable: true)).Should().BeEmpty();
        Evaluate(Facts(sitemapRequired: true, sitemapAvailable: false))
            .Single().RuleKey.Should().Be(RobotsRules.SitemapMissing);
    }

    [Fact]
    public void Evaluate_AppliesTheGroupWrittenForThisCrawler()
    {
        const string content = """
            User-agent: *
            Disallow:

            User-agent: webhealthmonitor
            Disallow: /
            """;

        Evaluate(Facts(content: content)).Single().RuleKey.Should().Be(RobotsRules.BlocksSite,
            "a group naming this crawler beats the wildcard group");
    }

    [Fact]
    public void Evaluate_BoundsFindingValuesSoAHostilePatternCannotFailTheSave()
    {
        var pattern = "/" + new string('p', 3000);

        var finding = Evaluate(Facts(content: $"User-agent: *\nDisallow: {pattern}"), path: pattern).Single();

        finding.ObservedValue!.Length.Should().BeLessThanOrEqualTo(FindingValues.MaxLength);
    }

    /// <summary>
    /// The transport sends "WebHealthMonitor/1.0"; a group naming that agent must win over the
    /// wildcard group, which it cannot if the evaluator invents its own shorter token.
    /// </summary>
    [Fact]
    public void Evaluate_UsesTheConfiguredTransportUserAgentToSelectTheGroup()
    {
        const string content = """
            User-agent: *
            Disallow:

            User-agent: WebHealthMonitor/1.0
            Disallow: /
            """;

        RobotsRuleEvaluator.Evaluate(Facts(content: content), "WebHealthMonitor/1.0", "/status", Policy())
            .Should().ContainSingle().Which.RuleKey.Should().Be(RobotsRules.BlocksSite);
    }

    [Fact]
    public void RobotsRules_HaveDistinctIssueKeys()
    {
        var keys = new[]
        {
            RobotsRules.BlocksSite, RobotsRules.BlocksEndpoint,
            RobotsRules.Unavailable, RobotsRules.SitemapMissing
        };

        keys.Should().OnlyHaveUniqueItems();
        keys.Select(key => HttpIssueIdentity.Create(key)).Should().OnlyHaveUniqueItems();
    }
}

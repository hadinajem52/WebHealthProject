using FluentAssertions;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// §11.2 asks the SEO Configuration report for "title, description, canonical, indexing, robots
/// and sitemap findings". Every SEO rule key shares the "Seo." prefix, so the report can only name
/// those six subjects if the grouping below is right.
/// </summary>
public sealed class SeoFindingGroupTests
{
    [Theory]
    [InlineData(RobotsRules.BlocksSite, SeoFindingGroups.Robots)]
    [InlineData(RobotsRules.BlocksEndpoint, SeoFindingGroups.Robots)]
    [InlineData(RobotsRules.Unavailable, SeoFindingGroups.Robots)]
    [InlineData(RobotsRules.SitemapMissing, SeoFindingGroups.Sitemap)]
    [InlineData(SeoRules.TitleMissing, SeoFindingGroups.Title)]
    [InlineData(SeoRules.TitleDuplicate, SeoFindingGroups.Title)]
    [InlineData(SeoRules.DescriptionMissing, SeoFindingGroups.Description)]
    [InlineData(SeoRules.CanonicalInvalid, SeoFindingGroups.Canonical)]
    [InlineData(SeoRules.CanonicalNotAbsolute, SeoFindingGroups.Canonical)]
    [InlineData(SeoRules.CanonicalDuplicate, SeoFindingGroups.Canonical)]
    [InlineData(SeoRules.CanonicalUnexpectedHost, SeoFindingGroups.Canonical)]
    [InlineData(SeoRules.NoIndexUnexpected, SeoFindingGroups.Indexing)]
    [InlineData(SeoRules.IndexableUnexpected, SeoFindingGroups.Indexing)]
    public void EveryRule_ReportsTheSubjectItIsAbout(string ruleKey, string expected) =>
        SeoFindingGroups.Of(ruleKey).Should().Be(expected);

    /// <summary>
    /// The sitemap is its own subject rather than part of robots. It is discovered through a
    /// robots directive, but a missing sitemap and a robots.txt that disallows the whole origin
    /// are not the same problem and do not carry the same urgency.
    /// </summary>
    [Fact]
    public void SitemapIsNotFiledUnderRobots() =>
        SeoFindingGroups.Of(RobotsRules.SitemapMissing).Should().NotBe(SeoFindingGroups.Robots);

    [Theory]
    [InlineData(RobotsRules.BlocksSite, true)]
    [InlineData(RobotsRules.SitemapMissing, true)]
    [InlineData(SeoRules.TitleMissing, false)]
    [InlineData(SeoRules.CanonicalInvalid, false)]
    public void SiteWideRulesAreToldApartFromPageRules(string ruleKey, bool expected) =>
        SeoFindingGroups.IsSiteWide(ruleKey).Should().Be(expected);

    [Fact]
    public void AnUnknownRuleStillGroups() =>
        SeoFindingGroups.Of("Seo.SomethingAddedLater").Should().Be(SeoFindingGroups.Other);

    /// <summary>
    /// The list item derives its count from the findings it holds, so the badge and the rules
    /// behind it cannot drift apart.
    /// </summary>
    [Fact]
    public void ListItem_GroupsFindings_AndLeadsWithTheSiteWideOnes()
    {
        var item = Item([SeoRules.TitleMissing, RobotsRules.BlocksSite, SeoRules.CanonicalInvalid]);

        item.OpenFindingCount.Should().Be(3);
        // A robots.txt blocking the origin outranks a page-level detail, so it is listed first.
        item.FindingGroups.Select(group => group.Group).Should()
            .Equal([SeoFindingGroups.Robots, SeoFindingGroups.Canonical, SeoFindingGroups.Title]);
        item.FindingGroups.Should().OnlyContain(group => group.Count == 1);
    }

    [Fact]
    public void ListItem_CountsRepeatedSubjectsTogether()
    {
        var item = Item([SeoRules.CanonicalInvalid, SeoRules.CanonicalDuplicate]);

        item.FindingGroups.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SeoFindingGroupCount(SeoFindingGroups.Canonical, 2));
    }

    [Fact]
    public void ListItem_WithNoFindings_HasNoGroups()
    {
        var item = Item([]);

        item.OpenFindingCount.Should().Be(0);
        item.FindingGroups.Should().BeEmpty();
    }

    private static SeoListItem Item(string[] ruleKeys) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "https://example.test/", "Site", "Production",
            IsProduction: true, SeoApplicabilities.Applicable, NotApplicableReason: null,
            DocumentTruncated: false, "Title", 5, 1, "Description", 11,
            CanonicalAbsoluteUrl: null, 0, RobotsMeta: null,
            SeoIndexingExpectations.Default, ruleKeys, DateTimeOffset.UnixEpoch);
}

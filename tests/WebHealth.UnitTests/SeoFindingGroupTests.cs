using FluentAssertions;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using Xunit;

namespace WebHealth.UnitTests;

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

    [Fact]
    public void ListItem_GroupsFindings_AndLeadsWithTheSiteWideOnes()
    {
        var item = Item([SeoRules.TitleMissing, RobotsRules.BlocksSite, SeoRules.CanonicalInvalid]);

        item.OpenFindingCount.Should().Be(3);
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

    [Theory]
    [MemberData(nameof(SelectableSubjects))]
    public void FilteringKeysAndDescribedGroupAgree(string subject)
    {
        var keys = SeoFindingGroups.RuleKeysFor(subject);

        keys.Should().NotBeEmpty("a selectable subject must have rules to filter on");
        keys.Should().OnlyContain(key => SeoFindingGroups.Of(key) == subject);
    }

    [Fact]
    public void TheFallbackGroupIsNotOfferedAsAFilter()
    {
        SeoFindingGroups.IsSelectable(SeoFindingGroups.Other).Should().BeFalse();
        SeoFindingGroups.RuleKeysFor(SeoFindingGroups.Other).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Everything")]
    [InlineData("robots")]
    public void AnUnrecognisedSubjectIsNotSelectable(string? subject) =>
        SeoFindingGroups.IsSelectable(subject).Should().BeFalse(
            "an unrecognised value becomes no filter rather than a query matching nothing");

    public static TheoryData<string> SelectableSubjects => [.. SeoFindingGroups.Selectable];

    private static SeoListItem Item(string[] ruleKeys) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "https://example.test/", "Site", "Production",
            IsProduction: true, SeoApplicabilities.Applicable, NotApplicableReason: null,
            DocumentTruncated: false, "Title", 5, 1, "Description", 11,
            CanonicalAbsoluteUrl: null, 0, RobotsMeta: null,
            SeoIndexingExpectations.Default, ruleKeys, DateTimeOffset.UnixEpoch);
}

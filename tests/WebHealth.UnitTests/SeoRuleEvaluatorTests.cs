using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-E02, BR-E03, BR-E04, BR-E05 and BR-E09, as written down in
/// docs/phase-6/SEO_Canonical_And_Indexing_Policy.md.
/// </summary>
public sealed class SeoRuleEvaluatorTests
{
    private const string Host = "example.test";

    private static SeoPolicy Policy(
        string? expectedHost = null,
        string expectation = SeoIndexingExpectations.Default,
        bool descriptionRequired = true,
        bool isProduction = true) =>
        new(expectedHost ?? Host, expectation, descriptionRequired, isProduction);

    private static SeoExtraction Applicable(
        string? title = "Title",
        int titleCount = 1,
        string? description = "Description",
        int descriptionCount = 1,
        string? canonical = null,
        int canonicalCount = 0,
        string? canonicalAbsolute = null,
        string? robots = null,
        bool truncated = false) =>
        new(SeoApplicabilities.Applicable, null, truncated,
            Value(title), titleCount,
            Value(description), descriptionCount,
            Value(canonical), canonicalCount, canonicalAbsolute,
            Value(robots), robots is null ? 0 : 1);

    private static SeoValue Value(string? value) => value is null ? SeoValue.None : new(value, value.Length);

    private static IReadOnlyList<string> RuleKeys(SeoExtraction extraction, SeoPolicy policy) =>
        SeoRuleEvaluator.Evaluate(extraction, policy).Select(finding => finding.RuleKey).ToArray();

    [Fact]
    public void Evaluate_ProducesNothingForAWellConfiguredPage() =>
        SeoRuleEvaluator.Evaluate(
            Applicable(canonical: $"https://{Host}/page", canonicalCount: 1,
                canonicalAbsolute: $"https://{Host}/page"),
            Policy()).Should().BeEmpty();

    [Fact]
    public void Evaluate_ProducesNothingForANotApplicableObservation() =>
        SeoRuleEvaluator.Evaluate(
            SeoExtraction.NotApplicable(SeoNotApplicableReasons.NonHtml), Policy())
            .Should().BeEmpty("there are no facts to judge, and the reason is the record of why");

    [Fact]
    public void Evaluate_ReportsAMissingTitle() =>
        RuleKeys(Applicable(title: null, titleCount: 0), Policy()).Should().Contain(SeoRules.TitleMissing);

    [Fact]
    public void Evaluate_ReportsDuplicateTitlesSeparatelyFromAMissingOne()
    {
        RuleKeys(Applicable(titleCount: 2), Policy())
            .Should().Contain(SeoRules.TitleDuplicate).And.NotContain(SeoRules.TitleMissing);

        // A page with two title elements, the first empty, is both missing and duplicated.
        RuleKeys(Applicable(title: null, titleCount: 2), Policy())
            .Should().Contain([SeoRules.TitleMissing, SeoRules.TitleDuplicate]);
    }

    [Fact]
    public void Evaluate_ReportsAMissingDescriptionOnlyWhenTheEndpointRequiresOne()
    {
        RuleKeys(Applicable(description: null, descriptionCount: 0), Policy())
            .Should().Contain(SeoRules.DescriptionMissing);

        RuleKeys(Applicable(description: null, descriptionCount: 0), Policy(descriptionRequired: false))
            .Should().NotContain(SeoRules.DescriptionMissing, "BR-E03 lets the endpoint disable the rule");
    }

    [Fact]
    public void Evaluate_DoesNotReportAMissingCanonical() =>
        RuleKeys(Applicable(), Policy()).Should().NotContain(key => key.StartsWith("Seo.Canonical"),
            "BR-E04 governs canonicals that exist; requiring one would fire across a healthy site");

    [Fact]
    public void Evaluate_ReportsARelativeCanonicalAsNotAbsolute()
    {
        var findings = RuleKeys(
            Applicable(canonical: "/page", canonicalCount: 1, canonicalAbsolute: $"https://{Host}/page"),
            Policy());

        findings.Should().Contain(SeoRules.CanonicalNotAbsolute)
            .And.NotContain(SeoRules.CanonicalUnexpectedHost, "it resolved to the expected host");
    }

    [Fact]
    public void Evaluate_ReportsAnUnresolvableCanonicalAsInvalidAndStopsThere()
    {
        var findings = RuleKeys(
            Applicable(canonical: "javascript:alert(1)", canonicalCount: 1, canonicalAbsolute: null),
            Policy());

        findings.Should().Contain(SeoRules.CanonicalInvalid)
            .And.NotContain(SeoRules.CanonicalUnexpectedHost, "there is no host to compare");
    }

    [Fact]
    public void Evaluate_ReportsACanonicalElementWithAnEmptyHrefAsInvalid()
    {
        var findings = SeoRuleEvaluator.Evaluate(
            Applicable(canonical: null, canonicalCount: 1, canonicalAbsolute: null), Policy());

        findings.Should().ContainSingle().Which.RuleKey.Should().Be(SeoRules.CanonicalInvalid,
            "a page that states a canonical and states nothing usable is invalid, not silent");
    }

    [Fact]
    public void Evaluate_BoundsFindingValuesSoAHostileValueCannotFailTheSave()
    {
        var canonical = "https://other.test/" + new string('p', 3000);

        var finding = SeoRuleEvaluator.Evaluate(
            Applicable(canonical: canonical, canonicalCount: 1, canonicalAbsolute: canonical), Policy())
            .Single(item => item.RuleKey == SeoRules.CanonicalUnexpectedHost);

        finding.ObservedValue!.Length.Should().BeLessThanOrEqualTo(FindingValues.MaxLength);
        finding.ExpectedValue!.Length.Should().BeLessThanOrEqualTo(FindingValues.MaxLength);
    }

    [Fact]
    public void Evaluate_ReportsDuplicateCanonicals() =>
        RuleKeys(
            Applicable(canonical: $"https://{Host}/a", canonicalCount: 2, canonicalAbsolute: $"https://{Host}/a"),
            Policy()).Should().Contain(SeoRules.CanonicalDuplicate);

    [Theory]
    [InlineData(true, FindingSeverities.High)]
    [InlineData(false, FindingSeverities.Warning)]
    public void Evaluate_RaisesCrossDomainCanonicalHigherOnProduction(bool isProduction, string expected)
    {
        var finding = SeoRuleEvaluator.Evaluate(
            Applicable(canonical: "https://other.test/a", canonicalCount: 1,
                canonicalAbsolute: "https://other.test/a"),
            Policy(isProduction: isProduction, expectation: SeoIndexingExpectations.Indexable))
            .Single(item => item.RuleKey == SeoRules.CanonicalUnexpectedHost);

        finding.Severity.Should().Be(expected);
        finding.ObservedValue.Should().Be("other.test");
        finding.ExpectedValue.Should().Be(Host);
    }

    [Fact]
    public void Evaluate_HonoursAnExpectedCanonicalHostOverride() =>
        RuleKeys(
            Applicable(canonical: "https://cdn.test/a", canonicalCount: 1, canonicalAbsolute: "https://cdn.test/a"),
            Policy(expectedHost: "cdn.test")).Should().NotContain(SeoRules.CanonicalUnexpectedHost);

    [Fact]
    public void Evaluate_ComparesTheCanonicalHostCaseInsensitively() =>
        RuleKeys(
            Applicable(canonical: $"https://{Host.ToUpperInvariant()}/a", canonicalCount: 1,
                canonicalAbsolute: $"https://{Host}/a"),
            Policy()).Should().NotContain(SeoRules.CanonicalUnexpectedHost);

    [Fact]
    public void Evaluate_ReportsNoIndexOnAProductionPageByDefault()
    {
        var finding = SeoRuleEvaluator.Evaluate(Applicable(robots: "noindex, follow"), Policy())
            .Single(item => item.RuleKey == SeoRules.NoIndexUnexpected);

        finding.Severity.Should().Be(FindingSeverities.High, "BR-E05 on production");
    }

    [Fact]
    public void Evaluate_AcceptsNoIndexWhenTheEndpointExplicitlyExpectsIt() =>
        RuleKeys(Applicable(robots: "noindex"), Policy(expectation: SeoIndexingExpectations.NoIndex))
            .Should().BeEmpty("BR-E05: expected-noindex pages pass");

    [Fact]
    public void Evaluate_ReportsAnIndexableNonProductionPageByDefault()
    {
        var finding = SeoRuleEvaluator.Evaluate(Applicable(), Policy(isProduction: false))
            .Single(item => item.RuleKey == SeoRules.IndexableUnexpected);

        finding.Severity.Should().Be(FindingSeverities.Warning, "BR-E09 reverses the production policy");
        finding.ExpectedValue.Should().Be("noindex");
    }

    [Fact]
    public void Evaluate_AcceptsAnIndexableNonProductionPageWhenTheEndpointSaysSo() =>
        RuleKeys(Applicable(), Policy(isProduction: false, expectation: SeoIndexingExpectations.Indexable))
            .Should().BeEmpty();

    [Theory]
    [InlineData("noindex", true)]
    [InlineData("NOINDEX", true)]
    [InlineData("none", true)]
    [InlineData("index, none", true)]
    [InlineData("  noindex , nofollow ", true)]
    [InlineData("noindexing", false)]
    [InlineData("index, follow", false)]
    [InlineData("nofollow", false)]
    [InlineData(null, false)]
    public void IsNoIndex_ReadsDirectiveTokensRatherThanSubstrings(string? robots, bool expected) =>
        SeoRuleEvaluator.IsNoIndex(robots).Should().Be(expected);

    [Fact]
    public void Evaluate_SuppressesAbsenceRulesOnATruncatedDocumentButKeepsPresenceRules()
    {
        var truncated = Applicable(
            title: null, titleCount: 2, description: null, descriptionCount: 0,
            canonical: "https://other.test/a", canonicalCount: 2,
            canonicalAbsolute: "https://other.test/a", truncated: true);

        var keys = RuleKeys(truncated, Policy());

        keys.Should().NotContain([SeoRules.TitleMissing, SeoRules.DescriptionMissing],
            "a body cut short is not evidence that a value was absent");
        keys.Should().Contain([
            SeoRules.TitleDuplicate, SeoRules.CanonicalDuplicate, SeoRules.CanonicalUnexpectedHost],
            "what was seen was really there");
    }

    [Fact]
    public void Evaluate_SuppressesTheIndexableRuleOnATruncatedNonProductionDocument() =>
        RuleKeys(Applicable(truncated: true), Policy(isProduction: false))
            .Should().NotContain(SeoRules.IndexableUnexpected);

    [Fact]
    public void Evaluate_StillReportsAnUnexpectedNoIndexOnATruncatedDocument() =>
        RuleKeys(Applicable(robots: "noindex", truncated: true), Policy())
            .Should().Contain(SeoRules.NoIndexUnexpected, "the directive was actually seen");

    [Fact]
    public void SeoRules_HaveDistinctIssueKeysSoIncidentsStayIndependent()
    {
        var keys = new[]
        {
            SeoRules.TitleMissing, SeoRules.TitleDuplicate, SeoRules.DescriptionMissing,
            SeoRules.CanonicalNotAbsolute, SeoRules.CanonicalInvalid, SeoRules.CanonicalDuplicate,
            SeoRules.CanonicalUnexpectedHost, SeoRules.NoIndexUnexpected, SeoRules.IndexableUnexpected
        };

        keys.Should().OnlyHaveUniqueItems();
        keys.Select(key => HttpIssueIdentity.Create(key)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Evaluate_UsesSeoFailureCategoriesOnly() =>
        SeoRuleEvaluator.Evaluate(
            Applicable(title: null, titleCount: 0, description: null, descriptionCount: 0,
                canonical: "https://other.test/a", canonicalCount: 2,
                canonicalAbsolute: "https://other.test/a", robots: "noindex"),
            Policy())
            .Select(finding => finding.FailureCategory)
            .Should().OnlyContain(category => SeoFailureCategories.All.Contains(category));

    [Theory]
    [InlineData(SeoIndexingExpectations.Default, true, SeoIndexingExpectations.Indexable)]
    [InlineData(SeoIndexingExpectations.Default, false, SeoIndexingExpectations.NoIndex)]
    [InlineData(SeoIndexingExpectations.NoIndex, true, SeoIndexingExpectations.NoIndex)]
    [InlineData(SeoIndexingExpectations.Indexable, false, SeoIndexingExpectations.Indexable)]
    public void Resolve_DerivesTheDefaultFromTheEnvironmentAndLetsAnExplicitValueWin(
        string configured, bool isProduction, string expected) =>
        SeoIndexingExpectations.Resolve(configured, isProduction).Should().Be(expected);
}

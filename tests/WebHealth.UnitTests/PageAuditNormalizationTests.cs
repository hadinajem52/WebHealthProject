using FluentAssertions;
using WebHealth.Domain.PageAudits;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// Every display mode Lighthouse can send has a deliberate meaning here. The mapping is the whole
/// feature's honesty: a manual audit shown as a failure would invent a problem the page does not
/// have, and a numeric audit shown as a pass would invent a threshold Lighthouse never published.
/// </summary>
public sealed class PageAuditNormalizationTests
{
    private static string Classify(string? mode, decimal? score = null, string? errorMessage = null) =>
        PageAuditNormalization.ClassifyAuditStatus(mode, score, errorMessage);

    [Fact]
    public void ClassifyAuditStatus_CallsAFullBinaryScorePassed() =>
        Classify(PageAuditScoreDisplayModes.Binary, 1m).Should().Be(PageAuditItemStatuses.Passed);

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(0.99)]
    public void ClassifyAuditStatus_CallsAShortBinaryScoreFailed(double score) =>
        Classify(PageAuditScoreDisplayModes.Binary, (decimal)score)
            .Should().Be(PageAuditItemStatuses.Failed);

    [Fact]
    public void ClassifyAuditStatus_CallsABinaryAuditWithNoScoreErrorRatherThanFailed() =>
        Classify(PageAuditScoreDisplayModes.Binary).Should().Be(PageAuditItemStatuses.Error,
            "the mode promises a pass or a fail and delivered neither; blaming the page would be a guess");

    [Fact]
    public void ClassifyAuditStatus_KeepsANumericAuditScoredRatherThanInventingAThreshold() =>
        Classify(PageAuditScoreDisplayModes.Numeric, 0.4m).Should().Be(PageAuditItemStatuses.Scored,
            "Lighthouse publishes no pass mark for numeric audits");

    [Fact]
    public void ClassifyAuditStatus_CallsAManualAuditManualRatherThanFailed() =>
        Classify(PageAuditScoreDisplayModes.Manual).Should().Be(PageAuditItemStatuses.Manual,
            "a check a person still has to do is not a check the page failed");

    [Fact]
    public void ClassifyAuditStatus_CallsANotApplicableAuditNotApplicable() =>
        Classify(PageAuditScoreDisplayModes.NotApplicable)
            .Should().Be(PageAuditItemStatuses.NotApplicable);

    [Fact]
    public void ClassifyAuditStatus_CallsAnInformativeAuditInformative() =>
        Classify(PageAuditScoreDisplayModes.Informative)
            .Should().Be(PageAuditItemStatuses.Informative);

    [Fact]
    public void ClassifyAuditStatus_CallsAnErrorModeError() =>
        Classify(PageAuditScoreDisplayModes.Error).Should().Be(PageAuditItemStatuses.Error);

    [Theory]
    [InlineData("somethingNew")]
    [InlineData("")]
    [InlineData(null)]
    public void ClassifyAuditStatus_TreatsAnUnknownModeAsErrorNeverAsFailed(string? mode) =>
        Classify(mode, 0m).Should().Be(PageAuditItemStatuses.Error,
            "Lighthouse can add a mode in any release; a mode we cannot read is not a failing page");

    [Fact]
    public void ClassifyAuditStatus_TreatsAnAuditCarryingAnErrorMessageAsErrorWhateverItsModeClaims() =>
        Classify(PageAuditScoreDisplayModes.Binary, 0m, "Required argument missing")
            .Should().Be(PageAuditItemStatuses.Error,
                "the audit did not run, so the page cannot be blamed for its result");

    [Fact]
    public void CountsAsFailure_CountsOnlyFailedAudits()
    {
        PageAuditNormalization.CountsAsFailure(PageAuditItemStatuses.Failed).Should().BeTrue();
        foreach (var status in new[]
        {
            PageAuditItemStatuses.Passed,
            PageAuditItemStatuses.Scored,
            PageAuditItemStatuses.Manual,
            PageAuditItemStatuses.NotApplicable,
            PageAuditItemStatuses.Informative,
            PageAuditItemStatuses.Error
        })
        {
            PageAuditNormalization.CountsAsFailure(status).Should().BeFalse(
                $"{status} is not a failing automated audit");
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.005, 1)]
    [InlineData(0.924, 92)]
    [InlineData(0.925, 93)]
    [InlineData(0.995, 100)]
    [InlineData(1, 100)]
    public void ToDisplayScore_RoundsAwayFromZeroAtEveryBoundary(double raw, int expected) =>
        PageAuditNormalization.ToDisplayScore((decimal)raw).Should().Be(expected);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(42)]
    public void NormalizeCategoryScore_RejectsAScoreOutsideTheProvidersOwnRange(double raw) =>
        PageAuditNormalization.NormalizeCategoryScore((decimal)raw).Should().BeNull(
            "a score we do not understand is not a low score");

    [Fact]
    public void NormalizeCategoryScore_KeepsAScoreInsideTheRange() =>
        PageAuditNormalization.NormalizeCategoryScore(0.92m).Should().Be(0.92m);

    [Fact]
    public void NormalizeCategoryScore_KeepsAMissingScoreMissing() =>
        PageAuditNormalization.NormalizeCategoryScore(null).Should().BeNull();

    [Fact]
    public void BoundText_LeavesTextInsideTheBoundUntouched() =>
        PageAuditNormalization.BoundText("Document has a meta description", 100)
            .Should().Be("Document has a meta description");

    [Fact]
    public void BoundText_MarksTextItHadToCut()
    {
        var bounded = PageAuditNormalization.BoundText(new string('a', 50), 10);

        bounded.Should().HaveLength(10);
        bounded.Should().EndWith("…", "text cut mid-sentence would read as the provider's own wording");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BoundText_TreatsBlankProviderTextAsAbsent(string? value) =>
        PageAuditNormalization.BoundText(value, 100).Should().BeNull();

    [Fact]
    public void SummarizeWarnings_ReturnsNullWhenThereWereNone() =>
        PageAuditNormalization.SummarizeWarnings([], 200).Should().BeNull();

    [Fact]
    public void SummarizeWarnings_JoinsAndBoundsTheWarningsItWasGiven() =>
        PageAuditNormalization.SummarizeWarnings(["first", "second"], 200)
            .Should().Be("first | second");

    [Theory]
    [InlineData("11.0.0", 11)]
    [InlineData("9.6.2", 9)]
    [InlineData("12", 12)]
    public void MajorVersionOf_ReadsTheLeadingSegment(string version, int expected) =>
        PageAuditNormalization.MajorVersionOf(version).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void MajorVersionOf_RefusesToGuessAtAVersionItCannotRead(string? version) =>
        PageAuditNormalization.MajorVersionOf(version).Should().BeNull();
}

using FluentAssertions;
using WebHealth.Application.Health;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// The confirmation engine and the incident automation both read issues through this one
/// function, so what it produces decides both what gets counted and what gets an incident.
/// </summary>
public sealed class CheckResultIssuesTests
{
    private static readonly DateTimeOffset MeasuredAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AHealthyResult_ObservesNoIssues()
    {
        Observe(HttpResultOutcomes.Healthy).Should().BeEmpty();
    }

    [Fact]
    public void ACancelledResult_ObservesNoIssues()
    {
        // A cancelled check is not evidence of anything, so it must not advance a counter.
        Observe(HttpResultOutcomes.Cancelled, Finding("Http.ServerError", FindingSeverities.Critical))
            .Should().BeEmpty();
    }

    [Fact]
    public void SlowResponse_CarriesItsOwnConfirmationCountAlongsideAvailability()
    {
        // BR-P03 next to BR-I04: one result, two issues, two different confirmation counts.
        var issues = Observe(
            HttpResultOutcomes.Critical,
            Finding("Http.ServerError", FindingSeverities.Critical),
            Finding(PerformanceRules.SlowResponse, FindingSeverities.Warning));

        issues.Should().HaveCount(2);
        issues.Single(issue => issue.IssueKey.Contains(PerformanceRules.SlowResponse))
            .FailureConfirmationCount.Should().Be(3);
        issues.Single(issue => issue.IssueKey.Contains("Http.ServerError"))
            .FailureConfirmationCount.Should().Be(2);
    }

    [Fact]
    public void SeveralFindingsOnOneIssueKey_CollapseToTheMostSevere()
    {
        var issues = Observe(
            HttpResultOutcomes.Critical,
            Finding("Http.ServerError", FindingSeverities.Warning),
            Finding("Http.ServerError", FindingSeverities.Critical));

        issues.Should().ContainSingle().Which.Severity.Should().Be(FindingSeverities.Critical);
    }

    [Fact]
    public void AFailureWithNoFinding_StillObservesSomethingToCount()
    {
        // An execution-terminal result has no finding. Observing nothing would make the
        // failure invisible to confirmation.
        var issues = Observe(HttpResultOutcomes.Critical);

        issues.Should().ContainSingle().Which.Severity.Should().Be(FindingSeverities.Critical);
        issues.Single().IssueKey.Should()
            .Be(HttpIssueIdentity.Create($"Http.{HttpFailureCategories.ExecutionExhausted}"));
    }

    private static IReadOnlyList<ObservedIssue> Observe(
        string outcome,
        params NormalizedFinding[] findings) =>
        CheckResultIssues.Observe(
            new NormalizedCheckResult(
                outcome,
                outcome == HttpResultOutcomes.Healthy ? null : HttpFailureCategories.ExecutionExhausted,
                null, 0, null, null, null, HttpResultNormalizer.MonitorSource, MeasuredAt, null,
                [], findings),
            monitorFailureConfirmationCount: 2);

    private static NormalizedFinding Finding(string ruleKey, string severity) => new(
        HttpFailureCategories.ServerError,
        ruleKey,
        severity,
        null,
        null,
        HttpIssueIdentity.Create(ruleKey));
}

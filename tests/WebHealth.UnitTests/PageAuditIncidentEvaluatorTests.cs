using FluentAssertions;
using WebHealth.Application;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class PageAuditIncidentEvaluatorTests
{
    [Fact]
    public void Evaluate_OpensCategoryIssueBelowConfiguredMinimum()
    {
        var policy = Policy() with { IncidentsEnabled = true, PerformanceMinimumScore = 90 };

        var result = PageAuditIncidentEvaluator.Evaluate(
            policy,
            PageAuditCategories.Performance,
            PageAuditStrategies.Mobile,
            0.71m,
            [],
            PageAuditIncidentIssueKeys.All);

        result.Observations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            IssueKey = PageAuditIncidentIssueKeys.CategoryScore(
                PageAuditCategories.Performance, PageAuditStrategies.Mobile),
            Severity = "Warning",
            Actual = 71m,
            Threshold = 90m,
            Unit = "score"
        });
    }

    [Fact]
    public void Evaluate_TreatsValueAtCategoryThresholdAsPassing()
    {
        var issueKey = PageAuditIncidentIssueKeys.CategoryScore(
            PageAuditCategories.Seo, PageAuditStrategies.Desktop);

        var result = PageAuditIncidentEvaluator.Evaluate(
            Policy(),
            PageAuditCategories.Seo,
            PageAuditStrategies.Desktop,
            0.90m,
            [],
            PageAuditIncidentIssueKeys.All);

        result.Observations.Should().BeEmpty();
        result.EvaluatedIssueKeys.Should().Contain(issueKey);
        result.IndeterminateIssueKeys.Should().NotContain(issueKey);
    }

    [Fact]
    public void Evaluate_OpensEnabledMetricIssueAboveMaximumAndLeavesMissingMetricsIndeterminate()
    {
        var policy = Policy() with
        {
            FirstContentfulPaintEnabled = true,
            LargestContentfulPaintEnabled = true
        };
        var item = new PageAuditProviderItem(
            PageAuditPerformanceMetrics.FirstContentfulPaint,
            "First Contentful Paint",
            null,
            0.42m,
            PageAuditScoreDisplayModes.Numeric,
            10,
            null,
            "2.6 s",
            null,
            null,
            2600,
            "millisecond");

        var result = PageAuditIncidentEvaluator.Evaluate(
            policy,
            PageAuditCategories.Performance,
            PageAuditStrategies.Mobile,
            0.95m,
            [item],
            PageAuditIncidentIssueKeys.All);

        var fcpKey = PageAuditIncidentIssueKeys.PerformanceMetric(
            PageAuditPerformanceMetrics.FirstContentfulPaint, PageAuditStrategies.Mobile);
        var lcpKey = PageAuditIncidentIssueKeys.PerformanceMetric(
            PageAuditPerformanceMetrics.LargestContentfulPaint, PageAuditStrategies.Mobile);
        result.Observations.Should().ContainSingle(observation => observation.IssueKey == fcpKey);
        result.Observations.Single().Severity.Should().Be("Critical");
        result.IndeterminateIssueKeys.Should().Contain(lcpKey);
    }

    [Fact]
    public void Evaluate_LeavesDisabledRulesIndeterminate()
    {
        var issueKey = PageAuditIncidentIssueKeys.CategoryScore(
            PageAuditCategories.Accessibility, PageAuditStrategies.Mobile);
        var policy = Policy() with { AccessibilityScoreEnabled = false };

        var result = PageAuditIncidentEvaluator.Evaluate(
            policy,
            PageAuditCategories.Accessibility,
            PageAuditStrategies.Mobile,
            0.20m,
            [],
            PageAuditIncidentIssueKeys.All);

        result.Observations.Should().BeEmpty();
        result.IndeterminateIssueKeys.Should().Contain(issueKey);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeThresholds()
    {
        var command = Command() with
        {
            SeoMinimumScore = 101,
            CumulativeLayoutShiftMaximum = 11
        };

        PageAuditIncidentEvaluator.Validate(command).Should().BeEquivalentTo([
            ValidationError.For(
                nameof(UpdatePageAuditIncidentPolicy.SeoMinimumScore),
                "The SEO minimum score is 101. Enter a score between 0 and 100."),
            ValidationError.For(
                nameof(UpdatePageAuditIncidentPolicy.CumulativeLayoutShiftMaximum),
                "The Cumulative Layout Shift maximum is 11 score. Enter a value between 0 and 10 score — around 0.1 score is typical.")]);
    }

    private static PageAuditIncidentPolicy Policy() => new(
        Guid.NewGuid(),
        false,
        true,
        90,
        true,
        90,
        true,
        90,
        true,
        90,
        false,
        1800,
        false,
        2500,
        false,
        200,
        false,
        0.1m,
        false,
        3400,
        1);

    private static UpdatePageAuditIncidentPolicy Command()
    {
        var policy = Policy();
        return new(
            policy.IncidentsEnabled,
            policy.PerformanceScoreEnabled,
            policy.PerformanceMinimumScore,
            policy.AccessibilityScoreEnabled,
            policy.AccessibilityMinimumScore,
            policy.BestPracticesScoreEnabled,
            policy.BestPracticesMinimumScore,
            policy.SeoScoreEnabled,
            policy.SeoMinimumScore,
            policy.FirstContentfulPaintEnabled,
            policy.FirstContentfulPaintMaximum,
            policy.LargestContentfulPaintEnabled,
            policy.LargestContentfulPaintMaximum,
            policy.TotalBlockingTimeEnabled,
            policy.TotalBlockingTimeMaximum,
            policy.CumulativeLayoutShiftEnabled,
            policy.CumulativeLayoutShiftMaximum,
            policy.SpeedIndexEnabled,
            policy.SpeedIndexMaximum,
            policy.Version);
    }
}

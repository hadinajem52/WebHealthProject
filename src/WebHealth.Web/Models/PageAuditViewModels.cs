using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Web.Models;

public sealed record PageAuditIndexViewModel(
    IReadOnlyList<EndpointOption> Endpoints,
    Guid? SelectedEndpointId,
    string SelectedCategory,
    string SelectedStrategy,
    PageAuditEndpointSummary? Summary,
    IReadOnlyList<PageAuditCategorySummary> CategorySummaries,
    IReadOnlyList<PageAuditRunSummary> Runs,
    IReadOnlyList<PageAuditItemView> Items,
    bool CanRunNow,
    bool CanArchive,
    EndpointTestBlock RunBlock = EndpointTestBlock.None)
{
    public bool AnyCategoryRunActive => CategorySummaries.Any(summary => summary.LatestRun?.IsActive == true);

    public IReadOnlyList<PageAuditItemView> PerformanceMetrics => SelectedCategory == PageAuditCategories.Performance
        ? [.. Items.Where(item => PageAuditPerformanceMetrics.All.Contains(item.AuditId)
            && item.NumericValue is not null)]
        : [];

    public IReadOnlyList<PageAuditSection> Sections =>
    [
        new("Failed audits", PageAuditItemStatuses.Failed,
            "Automated checks this page did not pass. These are what moved the score.",
            Of(PageAuditItemStatuses.Failed)),
        new("Manual checks", PageAuditItemStatuses.Manual,
            "Lighthouse cannot check these automatically. They do not affect the score, and a "
            + "person still has to look at them.",
            Of(PageAuditItemStatuses.Manual)),
        new("Audit errors", PageAuditItemStatuses.Error,
            "These audits could not run. Nothing here is a finding about the page.",
            Of(PageAuditItemStatuses.Error)),
        new("Scored audits", PageAuditItemStatuses.Scored,
            "Measured on a scale rather than passed or failed. Lighthouse publishes no pass mark "
            + "for these, so none is invented here.",
            Of(PageAuditItemStatuses.Scored)),
        new("Informative", PageAuditItemStatuses.Informative,
            "Reported for context and not scored.", Of(PageAuditItemStatuses.Informative)),
        new("Not applicable", PageAuditItemStatuses.NotApplicable,
            "Nothing on this page for the audit to examine. Not a pass.",
            Of(PageAuditItemStatuses.NotApplicable)),
        new("Passed audits", PageAuditItemStatuses.Passed,
            "Automated checks this page passed.", Of(PageAuditItemStatuses.Passed))
    ];

    private IReadOnlyList<PageAuditItemView> Of(string status) =>
        [.. Items.Where(item => item.Status == status
            && (status != PageAuditItemStatuses.Scored
                || !PageAuditPerformanceMetrics.All.Contains(item.AuditId)))];
}

public sealed record PageAuditSection(
    string Title,
    string Status,
    string Description,
    IReadOnlyList<PageAuditItemView> Items);

public static class PageAuditDisplay
{
    public static string ScoreTone(int? score) => score switch
    {
        null => "neutral",
        >= 90 => "success",
        >= 50 => "warning",
        _ => "danger"
    };

    public static string StatusTone(PageAuditRunSummary? run) => run?.Status switch
    {
        null => "neutral",
        PageAuditRunStatuses.Completed => "success",
        PageAuditRunStatuses.CompletedWithWarnings => "warning",
        PageAuditRunStatuses.Failed => "danger",
        PageAuditRunStatuses.Cancelled => "neutral",
        _ => "info"
    };

    public static string DescribeStatus(PageAuditRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Status switch
        {
            PageAuditRunStatuses.Queued => "Queued",
            PageAuditRunStatuses.Running => "Running",
            PageAuditRunStatuses.Completed => "Completed",
            PageAuditRunStatuses.CompletedWithWarnings => "Completed with warnings",
            PageAuditRunStatuses.Cancelled => "Cancelled",
            _ => "Failed"
        };
    }

    public static string DescribeStrategy(string strategy) => strategy switch
    {
        PageAuditStrategies.Desktop => "Desktop",
        _ => "Mobile"
    };

    public static string DescribeCategory(string category) => category switch
    {
        PageAuditCategories.Accessibility => "Accessibility",
        PageAuditCategories.BestPractices => "Best Practices",
        PageAuditCategories.Seo => "SEO",
        _ => "Performance"
    };

    public static string ItemTone(string status) => status switch
    {
        PageAuditItemStatuses.Passed => "success",
        PageAuditItemStatuses.Failed => "danger",
        PageAuditItemStatuses.Error => "warning",
        _ => "neutral"
    };

    public static string MetricLabel(string auditId) =>
        PageAuditMetricDefinitions.Get(auditId).Label;

    public static string MetricTone(PageAuditItemView item) =>
        ScoreTone(item.Score is null ? null : PageAuditNormalization.ToDisplayScore(item.Score.Value));

    public static string FormatMetric(PageAuditItemView item)
    {
        if (item.NumericValue is null)
        {
            return "Not measured";
        }

        if (item.AuditId == PageAuditPerformanceMetrics.CumulativeLayoutShift)
        {
            return item.NumericValue.Value.ToString("0.###");
        }

        if (item.AuditId == PageAuditPerformanceMetrics.TotalBlockingTime)
        {
            return $"{item.NumericValue.Value:0} ms";
        }

        return $"{item.NumericValue.Value / 1000:0.0} s";
    }

    public static string DescribeFailure(string? failureCategory) => failureCategory switch
    {
        PageAuditFailureCategories.ProviderRateLimited =>
            "Google rate-limited the request. The daily quota for the configured API key may be spent.",
        PageAuditFailureCategories.ProviderUnavailable =>
            "Google could not be reached, or answered with a server error.",
        PageAuditFailureCategories.ProviderTimeout =>
            "Google did not answer in time.",
        PageAuditFailureCategories.ProviderAuthenticationFailed =>
            "Google refused the API key. Check that it is valid and allows PageSpeed Insights.",
        PageAuditFailureCategories.TargetRejected =>
            "Google could not audit this URL. It may not be reachable from the public internet.",
        PageAuditFailureCategories.CaptchaBlocked =>
            "Google asked for a CAPTCHA instead of running the audit.",
        PageAuditFailureCategories.LighthouseRuntimeError =>
            "Lighthouse loaded the page but could not measure it.",
        PageAuditFailureCategories.ProviderContractInvalid
            or PageAuditFailureCategories.ProviderResponseInvalid =>
            "Google's response was not in a shape this application can store.",
        PageAuditFailureCategories.ProviderResponseTooLarge =>
            "Google's response was larger than the configured limit.",
        PageAuditFailureCategories.Cancelled => "The audit was cancelled before it finished.",
        null => "No reason was recorded.",
        _ => "The audit failed for an unrecognised reason."
    };

    public static string DescribeComparison(PageAuditComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (comparison.PreviousRunId is null)
        {
            return "No earlier audit to compare against yet.";
        }

        var direction = comparison.Delta switch
        {
            > 0 => $"up {comparison.Delta}",
            < 0 => $"down {Math.Abs(comparison.Delta!.Value)}",
            _ => "unchanged"
        };

        return comparison.SpansAVersionChange
            ? $"Score {direction} since the previous audit, which ran on a different Lighthouse "
                + "major version. Some of this change is the tool, not the page."
            : $"Score {direction} since the previous audit.";
    }
}

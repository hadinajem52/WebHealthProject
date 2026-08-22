using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;

namespace WebHealth.Web.Models;

/// <summary>The endpoint picker plus that endpoint's crawl history.</summary>
/// <param name="CanRunNow">
/// Whether to offer the Run crawl button. It mirrors the authorization the action itself
/// enforces -- the button is a convenience, not the control.
/// </param>
/// <param name="ActiveRunId">
/// The crawl already in flight for this endpoint, if any. One crawl per endpoint at a time is a
/// database constraint, so offering the button while one runs would only produce a refusal.
/// </param>
public sealed record CrawlIndexViewModel(
    IReadOnlyList<EndpointOption> Endpoints,
    Guid? SelectedEndpointId,
    IReadOnlyList<CrawlRunSummary> Runs,
    CrawlComparison Comparison,
    bool CanRunNow = false,
    Guid? ActiveRunId = null);

public sealed record EndpointOption(Guid Id, string Label);

public sealed record CrawlRunViewModel(
    CrawlRunSummary Run,
    IReadOnlyList<CrawlBrokenLink> BrokenLinks,
    int Offset,
    int PageSize)
{
    public bool HasMore => BrokenLinks.Count == PageSize;

    public int NextOffset => Offset + PageSize;

    public int PreviousOffset => Math.Max(0, Offset - PageSize);
}

/// <summary>
/// How a run is described to a reader. A run that stopped on a budget must never read as a clean
/// result, so the stop reason is rendered as its own statement rather than folded into the status.
/// </summary>
public static class CrawlRunDisplay
{
    public static string DescribeStatus(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Status switch
        {
            CrawlRunStatuses.Running => "Running",
            CrawlRunStatuses.Cancelled => "Cancelled",
            CrawlRunStatuses.Failed => "Failed",
            _ => run.CoveredWholeScope ? "Completed" : "Completed (partial)"
        };
    }

    public static string DescribeStopReason(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // A run in flight has not stopped, so it has no stop reason yet. The column defaults to
        // FrontierExhausted while running, which with nothing fetched so far would otherwise read
        // as "Fetched no pages - nothing on the site was examined": a verdict on a crawl that has
        // barely started. Nothing became visible here until crawls could actually be started.
        if (run.Status == CrawlRunStatuses.Running)
        {
            return run.PagesFetched == 0
                ? "In progress - no pages fetched yet"
                : $"In progress - {run.PagesFetched} page(s) fetched so far";
        }

        return run.StopReason switch
        {
            // An exhausted frontier with nothing fetched is a refusal, not a sweep. Saying
            // "covered the whole scope" beside a zero page count is the reading that turns a
            // blocked crawl into a clean bill of health.
            CrawlStopReasons.FrontierExhausted => run.PagesFetched > 0
                ? "Covered the whole scope"
                : "Fetched no pages — nothing on the site was examined",
            CrawlStopReasons.PageLimit => "Stopped at the page limit — the site was not fully covered",
            CrawlStopReasons.DurationLimit => "Stopped at the time limit — the site was not fully covered",
            CrawlStopReasons.Cancelled => "Cancelled — partial results only",
            _ => string.IsNullOrWhiteSpace(run.FailureReason)
                ? "Failed before it finished"
                : $"Failed before it finished — {Shortened(run.FailureReason)}"
        };
    }

    /// <summary>
    /// The whole recorded reason, for the run's own page. "It failed" with nothing behind it is a
    /// dead end for the one person who has to work out why, so the stored reason is shown rather
    /// than only logged.
    /// </summary>
    public static string DescribeFailure(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return string.IsNullOrWhiteSpace(run.FailureReason)
            ? "No reason was recorded. Check the application log for this run id."
            : run.FailureReason;
    }

    /// <summary>How much of a failure reason fits in a badge tooltip before it stops being read.</summary>
    private const int TooltipDetailLimit = 180;

    private static string Shortened(string detail) =>
        detail.Length <= TooltipDetailLimit
            ? detail
            : $"{detail[..TooltipDetailLimit].TrimEnd()}… Open the run for the full message.";

    public static string StatusTone(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status is CrawlRunStatuses.Failed) return "danger";

        // A run still going has not covered the whole scope *yet*, which is not the same as having
        // failed to. Without this it inherits the partial-crawl warning and reads as a problem
        // from the moment it starts.
        if (run.Status is CrawlRunStatuses.Running) return "info";

        if (run.Status is CrawlRunStatuses.Cancelled || !run.CoveredWholeScope) return "warning";
        return run.BrokenLinkCount > 0 ? "warning" : "success";
    }

    /// <summary>
    /// A comparison is only meaningful between two full-scope runs, and the reader refuses to make
    /// one otherwise. This says so on the page rather than rendering empty buckets that would read
    /// as "nothing changed".
    /// </summary>
    public static string DescribeComparison(CrawlComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (comparison.CurrentRunId is null)
        {
            return "No full-scope crawl has completed for this endpoint yet, so there is nothing to compare.";
        }

        return comparison.PreviousRunId is null
            ? "This is the first full-scope crawl, so every broken link is reported as new."
            : "Compared against the previous full-scope crawl.";
    }
}

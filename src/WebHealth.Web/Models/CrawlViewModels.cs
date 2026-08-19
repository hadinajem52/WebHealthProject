using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;

namespace WebHealth.Web.Models;

/// <summary>The endpoint picker plus that endpoint's crawl history.</summary>
public sealed record CrawlIndexViewModel(
    IReadOnlyList<EndpointOption> Endpoints,
    Guid? SelectedEndpointId,
    IReadOnlyList<CrawlRunSummary> Runs,
    CrawlComparison Comparison);

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
        return run.StopReason switch
        {
            CrawlStopReasons.FrontierExhausted => "Covered the whole scope",
            CrawlStopReasons.PageLimit => "Stopped at the page limit — the site was not fully covered",
            CrawlStopReasons.DurationLimit => "Stopped at the time limit — the site was not fully covered",
            CrawlStopReasons.Cancelled => "Cancelled — partial results only",
            _ => "Failed before it finished"
        };
    }

    public static string StatusTone(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status is CrawlRunStatuses.Failed) return "danger";
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

using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;

namespace WebHealth.Web.Models;

public sealed record CrawlIndexViewModel(
    IReadOnlyList<EndpointOption> Endpoints,
    Guid? SelectedEndpointId,
    IReadOnlyList<CrawlRunSummary> Runs,
    CrawlComparison Comparison,
    bool CanRunNow = false,
    Guid? ActiveRunId = null,
    EndpointTestBlock RunBlock = EndpointTestBlock.None,
    bool CrawlingAvailable = true);

public sealed record EndpointOption(Guid Id, string Label);

public sealed record CrawlRunViewModel(
    CrawlRunSummary Run,
    IReadOnlyList<CrawlBrokenLink> BrokenLinks,
    IReadOnlyList<CrawlSkipSummary> Skips,
    int Offset,
    int PageSize)
{
    public bool ExaminedNothing => Run.PagesFetched == 0 && Skips.Count > 0;

    public bool HasMore => BrokenLinks.Count == PageSize;

    public int NextOffset => Offset + PageSize;

    public int PreviousOffset => Math.Max(0, Offset - PageSize);
}

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

        if (run.Status == CrawlRunStatuses.Running)
        {
            return run.PagesFetched == 0
                ? "In progress - no pages fetched yet"
                : $"In progress - {run.PagesFetched} page(s) fetched so far";
        }

        return run.StopReason switch
        {
            CrawlStopReasons.FrontierExhausted when run.CoveredWholeScope => "Covered the whole scope",
            CrawlStopReasons.FrontierExhausted => run.PagesFetched == 0
                ? "Fetched no pages — nothing on the site was examined"
                : "Parts of the site could not be examined, so this crawl is partial",
            CrawlStopReasons.PageLimit => "Stopped at the page limit — the site was not fully covered",
            CrawlStopReasons.DurationLimit => "Stopped at the time limit — the site was not fully covered",
            CrawlStopReasons.Cancelled => "Cancelled — partial results only",
            _ => $"Failed before it finished — {SummarizeFailure(run.FailureReason)}"
        };
    }

    private static string SummarizeFailure(string? failureReason) => failureReason switch
    {
        CrawlFailureCodes.WorkerUnavailable => "no crawl worker was running",
        CrawlFailureCodes.Abandoned => "the process performing it is gone",
        CrawlFailureCodes.StorageUnavailable => "the database could not be reached",
        CrawlFailureCodes.SiteUnreachable => "the site stopped responding",
        CrawlFailureCodes.Unexpected or null or "" => "open the run for what is known",
        _ => IsAuthored(failureReason)
            ? Shortened(failureReason)
            : "open the run for what is known"
    };

    public static string DescribeFailure(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return DescribeFailureReason(run.FailureReason);
    }

    public static string DescribeFailureReason(string? failureReason) => failureReason switch
    {
        CrawlFailureCodes.WorkerUnavailable =>
            "No crawl worker is running on this instance, so the crawl was never started. "
            + "Enable Crawling:Scheduling and run it again.",
        CrawlFailureCodes.Abandoned =>
            "The crawl was still running long after its time limit, so the process performing it "
            + "is gone. Run it again; if it keeps happening, lower the page or time limit.",
        CrawlFailureCodes.StorageUnavailable =>
            "The crawl could not reach the database while recording what it had found. "
            + "Whatever it recorded before that point is kept. Run it again.",
        CrawlFailureCodes.SiteUnreachable =>
            "The site stopped responding partway through, so the crawl could not finish. "
            + "Check that the site is up and reachable, then run it again.",
        CrawlFailureCodes.Unexpected =>
            "The crawl stopped on an unexpected error. Nothing further is recorded here; "
            + "the application log holds the detail for this run id.",
        null or "" => "No reason was recorded. The application log holds the detail for this run id.",
        _ => IsAuthored(failureReason)
            ? failureReason
            : "The crawl stopped on an unexpected error. Nothing further is recorded here; "
                + "the application log holds the detail for this run id."
    };

    private static bool IsAuthored(string failureReason) =>
        !failureReason.Contains(" -> ", StringComparison.Ordinal)
        && !failureReason.Contains("Exception: ", StringComparison.Ordinal);

    public static string DescribeSkipReason(string skipReason) => skipReason switch
    {
        CrawlSkipReasons.RobotsDisallowed =>
            "Blocked by robots.txt — the site's rules do not permit this crawler",
        CrawlSkipReasons.ExternalCheckDisabled =>
            "External link — this run did not opt in to checking links off the site",
        CrawlSkipReasons.ExternalCheckLimit =>
            "Past the limit on link checks this run may make",
        CrawlSkipReasons.PageLimit => "Past the page limit for one crawl",
        CrawlSkipReasons.QueryVariantCap =>
            "Too many query-string variants of one path had already been queued",
        CrawlSkipReasons.AlreadySeen => "Already recorded by this crawl",
        CrawlSkipReasons.RunStopped => "The run stopped before reaching it",
        _ => skipReason
    };

    public static string DescribeOverrideRefusal(string? refusedBecause) => refusedBecause switch
    {
        CrawlOverrideRefusals.NotRequested => "Not requested",
        CrawlOverrideRefusals.ProductionTarget =>
            "Refused — this run predates the current policy, and was a production target",
        CrawlOverrideRefusals.NoApprovedException =>
            "Refused — this run predates the current policy, and the origin carried no exception",
        null => "Not requested",
        _ => refusedBecause
    };

    private const int TooltipDetailLimit = 180;

    private static string Shortened(string detail) =>
        detail.Length <= TooltipDetailLimit
            ? detail
            : $"{detail[..TooltipDetailLimit].TrimEnd()}… Open the run for the full message.";

    public static string StatusTone(CrawlRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status is CrawlRunStatuses.Failed) return "danger";

        if (run.Status is CrawlRunStatuses.Running) return "info";

        if (run.Status is CrawlRunStatuses.Cancelled || !run.CoveredWholeScope) return "warning";
        return run.BrokenLinkCount > 0 ? "warning" : "success";
    }

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

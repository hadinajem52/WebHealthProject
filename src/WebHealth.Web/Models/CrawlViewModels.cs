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
/// <param name="RunBlock">
/// Why the button is not offered. A missing button with no reason reads as a defect, so the page
/// says what is missing instead.
/// </param>
/// <param name="CrawlingAvailable">
/// Whether crawling is switched on at all. When it is off no endpoint can be crawled, which is a
/// different sentence from anything wrong with this one.
/// </param>
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
    /// <summary>
    /// A crawl that requested no page at all was stopped by something, and the reader is owed the
    /// reason in the place they are already looking rather than in a log.
    /// </summary>
    public bool ExaminedNothing => Run.PagesFetched == 0 && Skips.Count > 0;

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
            CrawlStopReasons.FrontierExhausted when run.CoveredWholeScope => "Covered the whole scope",
            CrawlStopReasons.FrontierExhausted => run.PagesFetched == 0
                ? "Fetched no pages — nothing on the site was examined"
                // The frontier drained, and parts of the site were still never examined: a page
                // whose markup would not parse or that robots kept the crawler out of offered no
                // links to follow, so the crawl ran out of work rather than out of site.
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

    /// <summary>
    /// Why a URL was never requested, in the reader's terms. The stored values are a small fixed
    /// vocabulary, so an unrecognised one is shown as itself rather than hidden: a reason nobody
    /// has written a sentence for is still more use than silence.
    /// </summary>
    public static string DescribeSkipReason(string skipReason) => skipReason switch
    {
        CrawlSkipReasons.RobotsDisallowed =>
            "Blocked by robots.txt — the site's rules do not permit this crawler",
        CrawlSkipReasons.TargetNotAuthorized =>
            "No target authorization covers this host, so it was never contacted",
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

    /// <summary>
    /// Why a run's robots override was not granted. Runs now ask for one every time and are always
    /// granted it, so the two refusals below belong to runs recorded under the older policy — kept
    /// because an old report still has to explain itself to whoever opens it.
    /// </summary>
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

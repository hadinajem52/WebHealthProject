using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PngAudits;

namespace WebHealth.Web.Models;

public sealed record PngAuditIndexViewModel(
    IReadOnlyList<EndpointOption> Endpoints,
    Guid? SelectedEndpointId,
    IReadOnlyList<PngAuditRunView> Runs,
    bool CanExecute,
    bool AuditAvailable,
    EndpointTestBlock RunBlock = EndpointTestBlock.None)
{
    public PngAuditRunView? ActiveRun => Runs.FirstOrDefault(run =>
        PngAuditRunStatuses.IsActive(run.Status));

    public bool CanRunNow => CanExecute
        && AuditAvailable
        && RunBlock == EndpointTestBlock.None
        && ActiveRun is null;
}

public sealed record PngAuditRunViewModel(
    PngAuditRunView Run,
    PngAuditResultSummaryView Summary,
    PngAuditPage<PngAuditImageResultView> Images,
    IReadOnlyList<PngAuditCoverageReasonView> CoverageReasons,
    string Filter)
{
    public IReadOnlyList<PngAuditCoverageReasonView> CoverageFor(string area) =>
        [.. CoverageReasons.Where(reason => reason.Area == area)];

    public int PreviousOffset => Math.Max(0, Images.Offset - Images.Limit);

    public int NextOffset => Images.Offset + Images.Limit;
}

public static class PngAuditDisplay
{
    public static string DescribeStatus(PngAuditRunView run) => run.Status switch
    {
        PngAuditRunStatuses.Queued => "Queued",
        PngAuditRunStatuses.Running => "Running",
        PngAuditRunStatuses.Completed => "Completed",
        PngAuditRunStatuses.CompletedWithWarnings => "Completed with limited coverage",
        PngAuditRunStatuses.Cancelled => "Cancelled",
        _ => "Failed"
    };

    public static string StatusTone(PngAuditRunView run) => run.Status switch
    {
        PngAuditRunStatuses.Completed => "success",
        PngAuditRunStatuses.CompletedWithWarnings => "warning",
        PngAuditRunStatuses.Failed => "danger",
        PngAuditRunStatuses.Cancelled => "neutral",
        _ => "info"
    };

    public static string StatusIcon(string tone) => tone switch
    {
        "success" => "success",
        "warning" => "warning",
        "danger" => "error",
        "info" => "information",
        _ => "clock"
    };

    public static string DescribeFailure(PngAuditRunView run) => run.FailureCode switch
    {
        PngAuditFailureCodes.WorkerUnavailable =>
            "No PNG audit worker accepted this run. Check that PNG auditing and its worker are enabled.",
        PngAuditFailureCodes.TargetChanged =>
            "The endpoint URL or production classification changed after this run was queued.",
        PngAuditFailureCodes.TargetIneligible =>
            "The endpoint stopped being eligible for manual testing before this run began.",
        PngAuditFailureCodes.AttemptsExhausted =>
            "The run could not recover within its allowed execution attempts.",
        PngAuditFailureCodes.StorageUnavailable =>
            "The run could not continue recording results in the database.",
        PngAuditFailureCodes.Cancelled => "The run was cancelled before it finished.",
        PngAuditFailureCodes.Unexpected =>
            "The run stopped unexpectedly. The application log contains the detail for this run id.",
        _ => run.SafeDiagnostic ?? "No failure reason was recorded."
    };

    public static string DescribeFilter(string filter) => filter switch
    {
        PngAuditImageFilters.WebpCandidates => "WebP candidates",
        PngAuditImageFilters.UsesTransparency => "Uses transparency",
        PngAuditImageFilters.BelowWebpThreshold => "Below WebP threshold",
        PngAuditImageFilters.AnimatedPng => "Animated PNG",
        PngAuditImageFilters.NotAnalyzed => "Not analyzed",
        PngAuditImageFilters.NotPng => "Not PNG",
        _ => "All"
    };

    public static string DescribeClassification(PngAuditImageResultView image) =>
        image.Classification switch
        {
            PngAuditImageClassifications.OpaqueWebpCandidate =>
                "Opaque PNG — lossless WebP candidate",
            PngAuditImageClassifications.OpaqueBelowWebpThreshold =>
                "Opaque PNG — lossless WebP below threshold",
            PngAuditImageClassifications.UsesTransparency => "Uses transparency",
            PngAuditImageClassifications.AnimatedPng => "Animated PNG",
            PngAuditImageClassifications.NotPng => "Not a PNG",
            PngAuditImageClassifications.FetchFailed => "Image could not be fetched",
            PngAuditImageClassifications.HttpNonSuccess =>
                image.HttpStatusCode is { } status ? $"Image returned HTTP {status}" : "Image request failed",
            PngAuditImageClassifications.ResponseTruncated => "Image exceeded the response limit",
            PngAuditImageClassifications.UnsupportedBitDepth => "16-bit PNG is not compared",
            PngAuditImageClassifications.DimensionsExceeded => "Image dimensions exceeded the limit",
            PngAuditImageClassifications.PixelLimitExceeded => "Decoded pixel limit exceeded",
            PngAuditImageClassifications.DecodedMemoryExceeded => "Decoded memory limit exceeded",
            PngAuditImageClassifications.WebpComparisonFailed => "WebP comparison could not finish",
            PngAuditImageClassifications.DecodeFailed => "PNG could not be decoded",
            _ => "Image format could not be identified"
        };

    public static string ClassificationTone(PngAuditImageResultView image) =>
        image.Classification switch
        {
            PngAuditImageClassifications.OpaqueWebpCandidate => "info",
            PngAuditImageClassifications.OpaqueBelowWebpThreshold => "neutral",
            PngAuditImageClassifications.UsesTransparency => "neutral",
            PngAuditImageClassifications.AnimatedPng => "neutral",
            PngAuditImageClassifications.NotPng => "neutral",
            _ => "warning"
        };

    public static string DescribeTransparency(PngAuditImageResultView image) =>
        image.UsesTransparency switch
        {
            true => "Alpha used",
            false => "Opaque",
            null when image.Classification == PngAuditImageClassifications.AnimatedPng => "Unknown for animation",
            _ => "Not measured"
        };

    public static string DescribeCoverageReason(string reason) => reason switch
    {
        "PageLimit" => "page limit reached",
        "DepthLimit" => "crawl depth limit reached",
        "PageBodyTruncated" => "page body exceeded its limit",
        "NavigationReferenceLimit" => "page navigation reference limit reached",
        "ImageReferenceLimit" => "page image reference limit reached",
        "UniqueImageLimit" => "unique-image limit reached",
        "TotalPageBytesLimit" => "page-byte budget reached",
        "TotalImageBytesLimit" => "image-byte budget reached",
        "SourceMappingLimit" => "source-mapping limit reached",
        "RobotsDisallowed" => "robots.txt disallowed a page",
        "DurationLimit" => "run duration reached",
        "HttpAttemptLimit" => "HTTP request budget reached",
        "PageFetchFailed" => "a page could not be fetched",
        "PageHttpNonSuccess" => "a page returned a non-success status",
        "RedirectOutOfScope" => "a page redirected outside the allowed scope",
        "DocumentNotInspected" => "a document could not be inspected",
        "QueryVariantLimit" => "query-variant limit reached",
        _ => reason
    };

    public static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "Not measured";
        }

        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        if (bytes < 1024L * 1024)
        {
            return $"{bytes / 1024m:0.#} KB";
        }

        return $"{bytes / (1024m * 1024m):0.##} MB";
    }

    public static string FormatSavings(long? bytes, decimal? percent)
    {
        if (bytes is null || percent is null)
        {
            return "Not compared";
        }

        return bytes <= 0
            ? "No reduction"
            : $"{FormatBytes(bytes)} · {percent:0.#}%";
    }
}

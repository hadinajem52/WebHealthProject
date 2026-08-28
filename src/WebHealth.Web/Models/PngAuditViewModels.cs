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

public sealed record PngAuditResultsRegionViewModel(
    Guid RunId,
    string Filter,
    PngAuditPage<PngAuditImageResultView> Images)
{
    public int PreviousOffset => Math.Max(0, Images.Offset - Images.Limit);

    public int NextOffset => Images.Offset + Images.Limit;
}

public static class PngAuditDisplay
{
    public static string DescribeStatus(PngAuditRunView run) => DescribeStatus(run.Status);

    public static string DescribeStatus(string status) => status switch
    {
        PngAuditRunStatuses.Queued => "Queued",
        PngAuditRunStatuses.Running => "Running",
        PngAuditRunStatuses.Completed => "Completed",
        PngAuditRunStatuses.CompletedWithWarnings => "Completed with limited coverage",
        PngAuditRunStatuses.Cancelled => "Cancelled",
        _ => "Failed"
    };

    public static string LiveVersion(PngAuditRunView run) =>
        $"{run.Status}:{run.PagesDiscovered}:{run.ImagesDiscovered}:{run.ImagesAnalyzed}:{run.RecommendationCount}";

    public static string LiveVersion(PngAuditLiveStatus run) =>
        $"{run.Status}:{run.PagesDiscovered}:{run.ImagesDiscovered}:{run.ImagesAnalyzed}:{run.RecommendationCount}";

    public static string StatusTone(PngAuditRunView run) => run.Status switch
    {
        PngAuditRunStatuses.Completed => "success",
        PngAuditRunStatuses.CompletedWithWarnings => "success",
        PngAuditRunStatuses.Failed => "danger",
        PngAuditRunStatuses.Cancelled => "neutral",
        _ => "info"
    };

    public static string StatusIcon(PngAuditRunView run) =>
        run.Status == PngAuditRunStatuses.CompletedWithWarnings
            ? "information"
            : StatusIcon(StatusTone(run));

    public static string? DescribeStatusDetail(PngAuditRunView run)
    {
        if (run.Status != PngAuditRunStatuses.CompletedWithWarnings)
        {
            return null;
        }

        var areas = LimitedCoverageAreas(run);
        return areas.Count == 0
            ? "The run finished, but at least one coverage area was limited."
            : $"The run finished, but {JoinAreas(areas)} did not cover everything within this run's configured limits.";
    }

    private static List<string> LimitedCoverageAreas(PngAuditRunView run)
    {
        var areas = new List<string>(3);
        if (run.CrawlCoverageLimited)
        {
            areas.Add("the site crawl");
        }
        if (run.ImageAnalysisCoverageLimited)
        {
            areas.Add("image analysis");
        }
        if (run.SourceMappingCoverageLimited)
        {
            areas.Add("source mapping");
        }

        return areas;
    }

    private static string JoinAreas(List<string> areas) => areas.Count switch
    {
        1 => areas[0],
        2 => $"{areas[0]} and {areas[1]}",
        _ => $"{string.Join(", ", areas.Take(areas.Count - 1))} and {areas[^1]}"
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
        PngAuditImageFilters.TransparentBackground => "Transparent background",
        PngAuditImageFilters.BelowWebpThreshold => "Below WebP threshold",
        PngAuditImageFilters.ComparisonUnavailable => "Comparison unavailable",
        PngAuditImageFilters.AnimatedPng => "Animated PNG",
        PngAuditImageFilters.NotAnalyzed => "Not analyzed",
        PngAuditImageFilters.NotPng => "Not PNG",
        _ => "All"
    };

    public static string DescribeClassification(PngAuditImageResultView image) =>
        image.Classification switch
        {
            PngAuditImageClassifications.VerifiedWebpCandidate =>
                "Verified lossless WebP candidate",
            PngAuditImageClassifications.OptimizedPngPreferred =>
                "PNG optimization preferred",
            PngAuditImageClassifications.BelowWebpThreshold =>
                "No material verified saving",
            PngAuditImageClassifications.ComparisonUnavailable =>
                "Comparison unavailable — pending verified engine",
            PngAuditImageClassifications.HighBitDepthPng =>
                "16-bit PNG — no exact WebP equivalent",
            PngAuditImageClassifications.ColorProfileUnsupported =>
                "Color profile not safely transferable",
            PngAuditImageClassifications.AnimatedPng => "Animated PNG",
            PngAuditImageClassifications.NotPng => "Not a PNG",
            PngAuditImageClassifications.FetchFailed => "Image could not be fetched",
            PngAuditImageClassifications.HttpNonSuccess =>
                image.HttpStatusCode is { } status ? $"Image returned HTTP {status}" : "Image request failed",
            PngAuditImageClassifications.ResponseTruncated => "Image exceeded the response limit",
            PngAuditImageClassifications.DimensionsExceeded => "Image dimensions exceeded the limit",
            PngAuditImageClassifications.PixelLimitExceeded => "Decoded pixel limit exceeded",
            PngAuditImageClassifications.DecodedMemoryExceeded => "Decoded memory limit exceeded",
            PngAuditImageClassifications.DecodeFailed => "PNG could not be decoded",
            _ => "Image format could not be identified"
        };

    public static string ClassificationTone(PngAuditImageResultView image) =>
        image.Classification switch
        {
            PngAuditImageClassifications.VerifiedWebpCandidate => "info",
            PngAuditImageClassifications.OptimizedPngPreferred => "info",
            PngAuditImageClassifications.BelowWebpThreshold => "neutral",
            PngAuditImageClassifications.ComparisonUnavailable => "neutral",
            PngAuditImageClassifications.HighBitDepthPng => "neutral",
            PngAuditImageClassifications.ColorProfileUnsupported => "neutral",
            PngAuditImageClassifications.AnimatedPng => "neutral",
            PngAuditImageClassifications.NotPng => "neutral",
            _ => "warning"
        };

    public static string DescribeTransparency(PngAuditImageResultView image)
    {
        if (image.UsesTransparency is null)
        {
            return image.Classification == PngAuditImageClassifications.AnimatedPng
                ? "Unknown for animation"
                : "Not measured";
        }

        if (image.UsesTransparency is false)
        {
            return "Opaque";
        }

        if (image.HasTransparentBackground(PngTransparencyPolicy.MinBackgroundCoveragePercent))
        {
            return "Transparent background";
        }

        return image.FullyTransparentPixelCount is > 0
            ? "Transparency, no background"
            : "Semi-transparent only";
    }

    public static string DescribeComparisonSize(PngAuditImageResultView image) =>
        image.Classification == PngAuditImageClassifications.ComparisonUnavailable
            ? "Not verified"
            : FormatBytes(image.CandidateWebpBytes);

    public static string DescribeComparisonSaving(PngAuditImageResultView image) =>
        image.Classification == PngAuditImageClassifications.ComparisonUnavailable
            ? "Pending verified engine"
            : FormatSavings(image.OriginalSavingsBytes, image.OriginalSavingsPercent);

    public static string? DescribeTransparencyEvidence(PngAuditImageResultView image)
    {
        if (image.UsesTransparency is not true)
        {
            return null;
        }

        var parts = new List<string>(3);
        if (image.BackgroundTransparentPixelCount is > 0
            && image.BackgroundCoveragePercent is { } coverage)
        {
            parts.Add($"{image.BackgroundTransparentPixelCount:N0} background pixels ({coverage:0.##}%)");
        }
        if (image.SemiTransparentPixelCount is > 0)
        {
            parts.Add($"{image.SemiTransparentPixelCount:N0} semi-transparent");
        }
        if (image.FullyTransparentPixelCount is > 0)
        {
            parts.Add($"{image.FullyTransparentPixelCount:N0} fully transparent");
        }
        if (image.MinAlpha is { } minAlpha)
        {
            parts.Add($"min alpha {minAlpha}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

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

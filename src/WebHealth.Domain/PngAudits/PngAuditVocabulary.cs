namespace WebHealth.Domain.PngAudits;

public static class PngAuditRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithWarnings = "CompletedWithWarnings";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static bool IsActive(string value) => value is Queued or Running;

    public static bool IsTerminal(string value) =>
        value is Completed or CompletedWithWarnings or Failed or Cancelled;
}

public static class PngAuditSources
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";
}

public static class PngAuditFailureCodes
{
    public const string WorkerUnavailable = "WorkerUnavailable";
    public const string TargetChanged = "TargetChanged";
    public const string TargetIneligible = "TargetIneligible";
    public const string AttemptsExhausted = "AttemptsExhausted";
    public const string StorageUnavailable = "StorageUnavailable";
    public const string Cancelled = "Cancelled";
    public const string Unexpected = "Unexpected";

    public static bool IsDefined(string value) => value is
        WorkerUnavailable or TargetChanged or TargetIneligible or AttemptsExhausted
        or StorageUnavailable or Cancelled or Unexpected;
}

public static class PngAuditImageClassifications
{
    public const string FetchFailed = "FetchFailed";
    public const string HttpNonSuccess = "HttpNonSuccess";
    public const string ResponseTruncated = "ResponseTruncated";
    public const string NotPng = "NotPng";
    public const string IdentificationFailed = "IdentificationFailed";
    public const string DimensionsExceeded = "DimensionsExceeded";
    public const string PixelLimitExceeded = "PixelLimitExceeded";
    public const string DecodedMemoryExceeded = "DecodedMemoryExceeded";
    public const string AnimatedPng = "AnimatedPng";
    public const string DecodeFailed = "DecodeFailed";
    public const string HighBitDepthPng = "HighBitDepthPng";
    public const string ColorProfileUnsupported = "ColorProfileUnsupported";
    public const string ComparisonUnavailable = "ComparisonUnavailable";
    public const string VerifiedWebpCandidate = "VerifiedWebpCandidate";
    public const string OptimizedPngPreferred = "OptimizedPngPreferred";
    public const string BelowWebpThreshold = "BelowWebpThreshold";

    public static IReadOnlyList<string> Values { get; } =
    [
        FetchFailed,
        HttpNonSuccess,
        ResponseTruncated,
        NotPng,
        IdentificationFailed,
        DimensionsExceeded,
        PixelLimitExceeded,
        DecodedMemoryExceeded,
        AnimatedPng,
        DecodeFailed,
        HighBitDepthPng,
        ColorProfileUnsupported,
        ComparisonUnavailable,
        VerifiedWebpCandidate,
        OptimizedPngPreferred,
        BelowWebpThreshold
    ];

    public static IReadOnlyList<string> ComparisonOutcomes { get; } =
    [
        ComparisonUnavailable,
        VerifiedWebpCandidate,
        OptimizedPngPreferred,
        BelowWebpThreshold
    ];

    public static IReadOnlyList<string> NotAnalyzed { get; } =
    [
        FetchFailed,
        HttpNonSuccess,
        ResponseTruncated,
        IdentificationFailed,
        DimensionsExceeded,
        PixelLimitExceeded,
        DecodedMemoryExceeded,
        DecodeFailed
    ];
}

public static class PngAuditRecommendations
{
    public const string None = "None";
    public const string OptimizePng = "OptimizePng";
    public const string LosslessWebp = "LosslessWebp";
}

public static class PngAuditSuggestedFormats
{
    public const string Webp = "WebP";
    public const string Png = "PNG";
}

public static class PngTransparencyPolicy
{
    public const decimal MinBackgroundCoveragePercent = 1.0m;
}

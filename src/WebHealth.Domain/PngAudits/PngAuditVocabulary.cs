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
    public const string UnsupportedBitDepth = "UnsupportedBitDepth";
    public const string DimensionsExceeded = "DimensionsExceeded";
    public const string PixelLimitExceeded = "PixelLimitExceeded";
    public const string DecodedMemoryExceeded = "DecodedMemoryExceeded";
    public const string AnimatedPng = "AnimatedPng";
    public const string DecodeFailed = "DecodeFailed";
    public const string UsesTransparency = "UsesTransparency";
    public const string WebpComparisonFailed = "WebpComparisonFailed";
    public const string OpaqueWebpCandidate = "OpaqueWebpCandidate";
    public const string OpaqueBelowWebpThreshold = "OpaqueBelowWebpThreshold";
}

public static class PngAuditRecommendations
{
    public const string None = "None";
    public const string LosslessWebp = "LosslessWebp";
}

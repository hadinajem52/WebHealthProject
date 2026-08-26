namespace WebHealth.Domain.Crawling;

public static class CrawlLinkClassifications
{
    public const string Healthy = "Healthy";
    public const string Redirected = "Redirected";
    public const string Broken = "Broken";
    public const string Blocked = "Blocked";
    public const string Timeout = "Timeout";
    public const string Skipped = "Skipped";
    public const string Unknown = "Unknown";

    public static bool IsSupported(string value) =>
        value is Healthy or Redirected or Broken or Blocked or Timeout or Skipped or Unknown;

    public static IReadOnlyList<string> Indeterminate => [Timeout, Blocked, Skipped, Unknown];

    public static IReadOnlyList<string> Working => [Healthy, Redirected];
}

public static class CrawlSkipReasons
{
    public const string AlreadySeen = "AlreadySeen";
    public const string PageLimit = "PageLimit";
    public const string ExternalCheckLimit = "ExternalCheckLimit";
    public const string QueryVariantCap = "QueryVariantCap";
    public const string RobotsDisallowed = "RobotsDisallowed";

    public const string ExternalCheckDisabled = "ExternalCheckDisabled";

    public const string RunStopped = "RunStopped";

    public static IReadOnlyList<string> LimitsCoverage =>
        [PageLimit, QueryVariantCap, RobotsDisallowed];
}

public static class CrawlStopReasons
{
    public const string FrontierExhausted = "FrontierExhausted";

    public const string PageLimit = "PageLimit";
    public const string DurationLimit = "DurationLimit";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsSupported(string value) =>
        value is FrontierExhausted or PageLimit or DurationLimit or Cancelled or Failed;
}

public static class CrawlFailureCodes
{
    public const string WorkerUnavailable = "WorkerUnavailable";
    public const string Abandoned = "Abandoned";
    public const string StorageUnavailable = "StorageUnavailable";
    public const string SiteUnreachable = "SiteUnreachable";
    public const string Unexpected = "Unexpected";

    public static bool IsSupported(string value) =>
        value is WorkerUnavailable or Abandoned or StorageUnavailable
            or SiteUnreachable or Unexpected;
}

public static class CrawlRunStatuses
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsSupported(string value) =>
        value is Running or Completed or Cancelled or Failed;
}

public enum CrawlRequestOutcome
{
    Responded = 0,

    Timeout = 1,

    Blocked = 2,

    Failed = 3,

    Broken = 4
}

public sealed record CrawlRequestObservation(
    CrawlRequestOutcome Outcome,
    int? StatusCode,
    int RedirectCount);

public static class CrawlLinkClassifier
{
    public static string Classify(CrawlRequestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.Outcome switch
        {
            CrawlRequestOutcome.Timeout => CrawlLinkClassifications.Timeout,
            CrawlRequestOutcome.Blocked => CrawlLinkClassifications.Blocked,
            CrawlRequestOutcome.Broken => CrawlLinkClassifications.Broken,
            CrawlRequestOutcome.Failed => CrawlLinkClassifications.Unknown,
            _ => ClassifyStatus(observation)
        };
    }

    private static string ClassifyStatus(CrawlRequestObservation observation) => observation.StatusCode switch
    {
        null => CrawlLinkClassifications.Unknown,
        401 or 403 or 407 or 451 => CrawlLinkClassifications.Blocked,
        408 or 425 or 429 => CrawlLinkClassifications.Unknown,
        >= 500 => CrawlLinkClassifications.Unknown,
        >= 400 => CrawlLinkClassifications.Broken,

        >= 300 and <= 399 => CrawlLinkClassifications.Broken,
        >= 200 and <= 299 => observation.RedirectCount > 0
            ? CrawlLinkClassifications.Redirected
            : CrawlLinkClassifications.Healthy,
        _ => CrawlLinkClassifications.Unknown
    };
}

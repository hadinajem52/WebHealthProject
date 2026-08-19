namespace WebHealth.Domain.Crawling;

/// <summary>BR-L06. Every link a run touches ends in exactly one of these.</summary>
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
}

/// <summary>
/// Why a URL was never requested. A skip is a recorded decision with a reason, not a silent
/// absence: "the crawler did not look at this" and "this link is fine" must never look alike.
/// </summary>
public static class CrawlSkipReasons
{
    public const string AlreadySeen = "AlreadySeen";
    public const string PageLimit = "PageLimit";
    public const string ExternalCheckLimit = "ExternalCheckLimit";
    public const string QueryVariantCap = "QueryVariantCap";
    public const string RobotsDisallowed = "RobotsDisallowed";

    /// <summary>The run did not opt in to checking external targets (BR-L08 is permission).</summary>
    public const string ExternalCheckDisabled = "ExternalCheckDisabled";

    /// <summary>
    /// No target-authorization evidence covers this host and port. Following an arbitrary href
    /// through our own network position is the SSRF the policy exists to prevent, so the link is
    /// recorded unchecked rather than fetched.
    /// </summary>
    public const string TargetNotAuthorized = "TargetNotAuthorized";

    /// <summary>Discovered, but the run stopped before reaching it. Never mistaken for healthy.</summary>
    public const string RunStopped = "RunStopped";
}

/// <summary>BR-L05: a crawl stops gracefully and reports why it stopped.</summary>
public static class CrawlStopReasons
{
    /// <summary>The frontier drained. The only reason that means the site was covered.</summary>
    public const string FrontierExhausted = "FrontierExhausted";

    public const string PageLimit = "PageLimit";
    public const string DurationLimit = "DurationLimit";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsSupported(string value) =>
        value is FrontierExhausted or PageLimit or DurationLimit or Cancelled or Failed;
}

/// <summary>BR-L10: a cancelled run keeps its findings and is never labelled complete.</summary>
public static class CrawlRunStatuses
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsSupported(string value) =>
        value is Running or Completed or Cancelled or Failed;
}

/// <summary>
/// What happened at the transport level, reduced to the four cases classification depends on. The
/// domain names these itself rather than referencing the transport's failure enum, so the rule
/// stays a pure function that a unit test can drive without an HTTP stack.
/// </summary>
public enum CrawlRequestOutcome
{
    /// <summary>A response was received; the status code decides the rest.</summary>
    Responded = 0,

    /// <summary>The request timed out. Separated from a failure because a slow host is not a broken link.</summary>
    Timeout = 1,

    /// <summary>Refused before or during the request by policy — robots, SSRF, authorization.</summary>
    Blocked = 2,

    /// <summary>DNS, connection, TLS or protocol failure.</summary>
    Failed = 3
}

public sealed record CrawlRequestObservation(
    CrawlRequestOutcome Outcome,
    int? StatusCode,
    int RedirectCount);

public static class CrawlLinkClassifier
{
    /// <summary>
    /// BR-L06. A redirect that ends in success is <c>Redirected</c>, not <c>Healthy</c>: the link
    /// works, and it is also stale, and only reporting the second gets it fixed.
    /// <para>
    /// A 401 or 403 is <c>Blocked</c> rather than <c>Broken</c>. The resource exists and the
    /// crawler is not entitled to it; calling that a broken link would fill the report with every
    /// authenticated area of the site.
    /// </para>
    /// </summary>
    public static string Classify(CrawlRequestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.Outcome switch
        {
            CrawlRequestOutcome.Timeout => CrawlLinkClassifications.Timeout,
            CrawlRequestOutcome.Blocked => CrawlLinkClassifications.Blocked,
            CrawlRequestOutcome.Failed => CrawlLinkClassifications.Broken,
            _ => ClassifyStatus(observation)
        };
    }

    private static string ClassifyStatus(CrawlRequestObservation observation) => observation.StatusCode switch
    {
        null => CrawlLinkClassifications.Unknown,
        401 or 403 or 407 or 451 => CrawlLinkClassifications.Blocked,
        >= 400 => CrawlLinkClassifications.Broken,

        // A 3xx that is still a 3xx after the redirect budget was spent never reached a resource.
        >= 300 and <= 399 => CrawlLinkClassifications.Broken,
        >= 200 and <= 299 => observation.RedirectCount > 0
            ? CrawlLinkClassifications.Redirected
            : CrawlLinkClassifications.Healthy,
        _ => CrawlLinkClassifications.Unknown
    };
}

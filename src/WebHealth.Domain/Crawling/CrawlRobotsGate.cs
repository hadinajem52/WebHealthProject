using WebHealth.Domain.Seo;

namespace WebHealth.Domain.Crawling;

/// <summary>Why an override was refused. Recorded on the run; never a silent downgrade.</summary>
public static class CrawlOverrideRefusals
{
    public const string NotRequested = "NotRequested";
    public const string ProductionTarget = "ProductionTarget";
    public const string NoApprovedException = "NoApprovedException";
}

public readonly record struct CrawlOverrideDecision(bool Granted, string? RefusedBecause)
{
    public static CrawlOverrideDecision Refused(string reason) => new(false, reason);
}

/// <summary>
/// What the origin's stored snapshot (6.4) says, as facts. The crawler performs no robots fetch of
/// its own: the snapshot is refreshed per origin on its own schedule, and a run reads what is there.
/// </summary>
public sealed record CrawlRobotsFacts(bool HasSnapshot, string? Content, bool HasApprovedException)
{
    /// <summary>No snapshot yet is not a prohibition, for the same reason it raises no finding.</summary>
    public static CrawlRobotsFacts Unknown { get; } = new(false, null, false);
}

/// <summary>
/// BR-L02. Whether a crawl may fetch a path, and whether an override of a published restriction is
/// authorized. Both are pure functions of the stored snapshot and the run's own properties.
/// </summary>
public static class CrawlRobotsGate
{
    /// <summary>
    /// An override is granted only when the run asked for it, the target is non-production, and the
    /// origin carries the approved exception 6.4 records with its reason and approver. All three,
    /// every time: a production crawl never bypasses a published restriction, and an override
    /// nobody approved is not an override.
    /// </summary>
    public static CrawlOverrideDecision EvaluateOverride(
        bool requested,
        bool isProduction,
        CrawlRobotsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!requested) return CrawlOverrideDecision.Refused(CrawlOverrideRefusals.NotRequested);
        if (isProduction) return CrawlOverrideDecision.Refused(CrawlOverrideRefusals.ProductionTarget);
        return facts.HasApprovedException
            ? new(true, null)
            : CrawlOverrideDecision.Refused(CrawlOverrideRefusals.NoApprovedException);
    }

    /// <summary>
    /// Whether the path may be fetched. An origin with no snapshot, or one whose robots.txt has no
    /// groups, disallows nothing — which is also what a 404 for robots.txt means.
    /// </summary>
    public static bool IsAllowed(CrawlRobotsFacts facts, string userAgent, string path, bool overrideGranted)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (overrideGranted || !facts.HasSnapshot) return true;
        return RobotsTxtParser.Evaluate(RobotsTxtParser.Parse(facts.Content), userAgent, path).IsAllowed;
    }
}

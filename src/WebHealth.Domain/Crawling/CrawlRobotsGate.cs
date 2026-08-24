using WebHealth.Domain.Seo;

namespace WebHealth.Domain.Crawling;

/// <summary>Why an override was refused. Recorded on the run; never a silent downgrade.</summary>
public static class CrawlOverrideRefusals
{
    public const string NotRequested = "NotRequested";

    /// <summary>
    /// Only on runs recorded before the owner chose to bypass robots on every crawl. The reader of
    /// an old report is still owed the reason that run was refused, so the names stay.
    /// </summary>
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
/// applied. Both are pure functions of the stored snapshot and the run's own properties.
/// </summary>
public static class CrawlRobotsGate
{
    /// <summary>
    /// An override is granted whenever the run asks for it.
    /// <para>
    /// This is a deliberate narrowing of BR-L02, decided by the project owner on 2026-08-24: a
    /// broken-link crawl of one's own site should see the pages robots.txt hides from search
    /// engines, because a disallowed path can still be linked and still be broken. The conditions
    /// that used to gate it -- a non-production target and an approved per-origin exception --
    /// are gone, so nothing here refuses a crawl the caller asked for.
    /// </para>
    /// <para>
    /// What still bounds a crawl is unchanged and is where the real protection was: a host is only
    /// ever fetched with recorded target-authorization evidence, the per-host rate limiter still
    /// paces every request, and the run still records that it bypassed robots rather than
    /// reporting a clean sweep. Robots is a request from a site's owner, and the owner of these
    /// targets is the one asking.
    /// </para>
    /// </summary>
    public static CrawlOverrideDecision EvaluateOverride(bool requested) =>
        requested
            ? new(true, null)
            : CrawlOverrideDecision.Refused(CrawlOverrideRefusals.NotRequested);

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

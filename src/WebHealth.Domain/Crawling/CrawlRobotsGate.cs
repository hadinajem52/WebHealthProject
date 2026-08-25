using WebHealth.Domain.Seo;

namespace WebHealth.Domain.Crawling;

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

public sealed record CrawlRobotsFacts(bool HasSnapshot, string? Content, bool HasApprovedException)
{
    public static CrawlRobotsFacts Unknown { get; } = new(false, null, false);
}

public static class CrawlRobotsGate
{
    public static CrawlOverrideDecision EvaluateOverride(bool requested) =>
        requested
            ? new(true, null)
            : CrawlOverrideDecision.Refused(CrawlOverrideRefusals.NotRequested);

    public static bool IsAllowed(CrawlRobotsFacts facts, string userAgent, string path, bool overrideGranted)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (overrideGranted || !facts.HasSnapshot) return true;
        return RobotsTxtParser.Evaluate(RobotsTxtParser.Parse(facts.Content), userAgent, path).IsAllowed;
    }
}

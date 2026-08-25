using WebHealth.Application.Monitoring;
using WebHealth.Domain.Seo;

namespace WebHealth.Application.Seo;

public static class RobotsSnapshotStatuses
{
    public const string Fetched = "Fetched";

    public const string NotFound = "NotFound";

    public const string Unavailable = "Unavailable";

    public static bool IsSupported(string value) => value is Fetched or NotFound or Unavailable;
}

public static class RobotsRules
{
    public const string BlocksSite = "Seo.RobotsBlocksSite";
    public const string BlocksEndpoint = "Seo.RobotsBlocksEndpoint";
    public const string Unavailable = "Seo.RobotsUnavailable";
    public const string SitemapMissing = "Seo.SitemapMissing";

    public static IReadOnlyList<string> BlockingIssueKeys { get; } =
        [HttpIssueIdentity.Create(BlocksSite), HttpIssueIdentity.Create(BlocksEndpoint)];

    public static IReadOnlyList<string> AllIssueKeys { get; } =
        [.. BlockingIssueKeys, HttpIssueIdentity.Create(Unavailable), HttpIssueIdentity.Create(SitemapMissing)];
}

public sealed record RobotsSnapshotFacts(
    string Status,
    string? Content,
    bool HasApprovedException,
    bool SitemapRequired,
    bool SitemapAvailable);

public static class RobotsRuleEvaluator
{
    public static IReadOnlyList<NormalizedFinding> Evaluate(
        RobotsSnapshotFacts? facts,
        string userAgent,
        string endpointPath,
        SeoPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return facts is null ? [] : [.. Rules(facts, userAgent, endpointPath)];
    }

    private static IEnumerable<NormalizedFinding> Rules(
        RobotsSnapshotFacts facts,
        string userAgent,
        string endpointPath)
    {
        if (facts.Status == RobotsSnapshotStatuses.Unavailable)
        {
            yield return Finding(RobotsRules.Unavailable,
                "robots.txt could not be read", "A readable robots.txt", FindingSeverities.Warning);
        }
        else if (facts.Status == RobotsSnapshotStatuses.Fetched && !facts.HasApprovedException)
        {
            foreach (var finding in BlockingRules(facts.Content, userAgent, endpointPath))
            {
                yield return finding;
            }
        }

        if (facts.SitemapRequired && !facts.SitemapAvailable)
        {
            yield return Finding(RobotsRules.SitemapMissing,
                "No reachable sitemap", "A sitemap returning a success status", FindingSeverities.Warning);
        }
    }

    private static IEnumerable<NormalizedFinding> BlockingRules(
        string? content,
        string userAgent,
        string endpointPath)
    {
        var file = RobotsTxtParser.Parse(content);
        if (file.IsEmpty) yield break;

        var root = RobotsTxtParser.Evaluate(file, userAgent, "/");
        if (!root.IsAllowed)
        {
            yield return Finding(RobotsRules.BlocksSite,
                $"Disallow: {root.MatchedRule!.Pattern}", "A crawlable site root",
                FindingSeverities.Warning);
            yield break;
        }

        var endpoint = RobotsTxtParser.Evaluate(file, userAgent, endpointPath);
        if (!endpoint.IsAllowed)
        {
            yield return Finding(RobotsRules.BlocksEndpoint,
                $"Disallow: {endpoint.MatchedRule!.Pattern}", $"A crawlable {endpointPath}",
                FindingSeverities.Warning);
        }
    }

    private static NormalizedFinding Finding(string ruleKey, string observed, string expected, string severity) =>
        new(SeoFailureCategories.Robots, ruleKey, severity,
            FindingValues.Bound(observed), FindingValues.Bound(expected),
            HttpIssueIdentity.Create(ruleKey));
}

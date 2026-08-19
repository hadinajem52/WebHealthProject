using WebHealth.Application.Monitoring;
using WebHealth.Domain.Seo;

namespace WebHealth.Application.Seo;

public static class RobotsSnapshotStatuses
{
    /// <summary>The origin answered with a robots.txt, which may still be empty.</summary>
    public const string Fetched = "Fetched";

    /// <summary>The origin answered 404: a valid answer meaning nothing is disallowed.</summary>
    public const string NotFound = "NotFound";

    /// <summary>The fetch failed or the origin returned a server error: no answer at all.</summary>
    public const string Unavailable = "Unavailable";

    public static bool IsSupported(string value) => value is Fetched or NotFound or Unavailable;
}

public static class RobotsRules
{
    public const string BlocksSite = "Seo.RobotsBlocksSite";
    public const string BlocksEndpoint = "Seo.RobotsBlocksEndpoint";
    public const string Unavailable = "Seo.RobotsUnavailable";
    public const string SitemapMissing = "Seo.SitemapMissing";
}

/// <summary>
/// What the origin's stored snapshot says, as facts. The rules never fetch: BR-E06 evidence is
/// refreshed per origin on its own schedule, and a check reads what is already there.
/// </summary>
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

        // No snapshot yet means no evidence, not a clean bill of health. The first refresh is at
        // most one TTL away, and inventing a finding from an empty cache would fire on every new
        // origin the moment it is registered.
        return facts is null ? [] : [.. Rules(facts, userAgent, endpointPath, policy)];
    }

    private static IEnumerable<NormalizedFinding> Rules(
        RobotsSnapshotFacts facts,
        string userAgent,
        string endpointPath,
        SeoPolicy policy)
    {
        if (facts.Status == RobotsSnapshotStatuses.Unavailable)
        {
            yield return Finding(RobotsRules.Unavailable,
                "robots.txt could not be read", "A readable robots.txt", FindingSeverities.Warning);
        }
        else if (facts.Status == RobotsSnapshotStatuses.Fetched && !facts.HasApprovedException)
        {
            foreach (var finding in BlockingRules(facts.Content, userAgent, endpointPath, policy))
            {
                yield return finding;
            }
        }

        // A sitemap is only required where the origin says it is: most origins do not need one,
        // and a finding on every origin would be noise rather than a signal.
        if (facts.SitemapRequired && !facts.SitemapAvailable)
        {
            yield return Finding(RobotsRules.SitemapMissing,
                "No reachable sitemap", "A sitemap returning a success status", FindingSeverities.Warning);
        }
    }

    private static IEnumerable<NormalizedFinding> BlockingRules(
        string? content,
        string userAgent,
        string endpointPath,
        SeoPolicy policy)
    {
        var file = RobotsTxtParser.Parse(content);
        if (file.IsEmpty) yield break;

        var root = RobotsTxtParser.Evaluate(file, userAgent, "/");
        if (!root.IsAllowed)
        {
            // The only Critical in the SEO family: a production site telling every crawler to go
            // away is the whole site leaving search, not a detail to look at next sprint.
            yield return Finding(RobotsRules.BlocksSite,
                $"Disallow: {root.MatchedRule!.Pattern}", "A crawlable site root",
                policy.IsProduction ? FindingSeverities.Critical : FindingSeverities.Warning);
            yield break;
        }

        var endpoint = RobotsTxtParser.Evaluate(file, userAgent, endpointPath);
        if (!endpoint.IsAllowed)
        {
            yield return Finding(RobotsRules.BlocksEndpoint,
                $"Disallow: {endpoint.MatchedRule!.Pattern}", $"A crawlable {endpointPath}",
                policy.EnvironmentSeverity);
        }
    }

    private static NormalizedFinding Finding(string ruleKey, string observed, string expected, string severity) =>
        new(SeoFailureCategories.Robots, ruleKey, severity,
            FindingValues.Bound(observed), FindingValues.Bound(expected),
            HttpIssueIdentity.Create(ruleKey));
}

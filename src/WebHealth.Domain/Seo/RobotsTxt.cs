namespace WebHealth.Domain.Seo;

public sealed record RobotsRule(bool IsAllow, string Pattern)
{
    public int Specificity => Pattern.Length;
}

public sealed record RobotsGroup(IReadOnlyList<string> Agents, IReadOnlyList<RobotsRule> Rules);

public sealed record RobotsFile(IReadOnlyList<RobotsGroup> Groups, IReadOnlyList<string> Sitemaps)
{
    public static RobotsFile Empty { get; } = new([], []);

    public bool IsEmpty => Groups.Count == 0;
}

public readonly record struct RobotsDecision(bool IsAllowed, RobotsRule? MatchedRule)
{
    public static RobotsDecision Allowed => new(true, null);
}

public static class RobotsTxtParser
{
    public const string WildcardAgent = "*";

    private const string UserAgentDirective = "user-agent";
    private const string AllowDirective = "allow";
    private const string DisallowDirective = "disallow";
    private const string SitemapDirective = "sitemap";

    public static RobotsFile Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return RobotsFile.Empty;

        content = content.TrimStart('\uFEFF');

        var groups = new List<RobotsGroup>();
        var sitemaps = new List<string>();
        var agents = new List<string>();
        var rules = new List<RobotsRule>();

        var awaitingRules = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = StripComment(raw);
            if (line.Length == 0) continue;

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0) continue;

            var directive = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();

            switch (directive)
            {
                case UserAgentDirective when value.Length > 0:
                    if (awaitingRules)
                    {
                        groups.Add(new([.. agents], [.. rules]));
                        agents.Clear();
                        rules.Clear();
                        awaitingRules = false;
                    }

                    agents.Add(value.ToLowerInvariant());
                    break;

                case AllowDirective when agents.Count > 0 && value.Length > 0:
                    rules.Add(new(true, value));
                    awaitingRules = true;
                    break;

                case DisallowDirective when agents.Count > 0:
                    if (value.Length > 0) rules.Add(new(false, value));
                    awaitingRules = true;
                    break;

                case SitemapDirective when value.Length > 0:
                    sitemaps.Add(value);
                    break;

                default:
                    break;
            }
        }

        if (agents.Count > 0) groups.Add(new([.. agents], [.. rules]));
        return new(groups, sitemaps);
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return (hash < 0 ? line : line[..hash]).Trim();
    }

    public static IReadOnlyList<RobotsRule> RulesFor(RobotsFile file, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(userAgent);
        var agent = userAgent.ToLowerInvariant();

        var best = -1;
        foreach (var group in file.Groups)
        {
            foreach (var candidate in group.Agents)
            {
                if (candidate != WildcardAgent && agent.StartsWith(candidate, StringComparison.Ordinal))
                {
                    best = Math.Max(best, candidate.Length);
                }
            }
        }

        return best < 0
            ? [.. file.Groups.Where(group => group.Agents.Contains(WildcardAgent)).SelectMany(group => group.Rules)]
            : [.. file.Groups
                .Where(group => group.Agents.Any(candidate =>
                    candidate != WildcardAgent
                    && candidate.Length == best
                    && agent.StartsWith(candidate, StringComparison.Ordinal)))
                .SelectMany(group => group.Rules)];
    }

    public static RobotsDecision Evaluate(RobotsFile file, string userAgent, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var candidates = RulesFor(file, userAgent)
            .Where(rule => Matches(rule.Pattern, path))
            .ToArray();
        if (candidates.Length == 0) return RobotsDecision.Allowed;

        var winner = candidates
            .OrderByDescending(rule => rule.Specificity)
            .ThenByDescending(rule => rule.IsAllow)
            .First();
        return new(winner.IsAllow, winner);
    }

    public static bool Matches(string pattern, string path)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(path);
        if (pattern.Length == 0) return false;

        var anchored = pattern.EndsWith('$');
        var compiled = anchored ? pattern[..^1] : pattern + "*";

        int patternIndex = 0, pathIndex = 0, star = -1, resume = 0;
        while (pathIndex < path.Length)
        {
            if (patternIndex < compiled.Length && compiled[patternIndex] == '*')
            {
                star = patternIndex++;
                resume = pathIndex;
            }
            else if (patternIndex < compiled.Length && compiled[patternIndex] == path[pathIndex])
            {
                patternIndex++;
                pathIndex++;
            }
            else if (star >= 0)
            {
                patternIndex = star + 1;
                pathIndex = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < compiled.Length && compiled[patternIndex] == '*') patternIndex++;
        return patternIndex == compiled.Length;
    }
}

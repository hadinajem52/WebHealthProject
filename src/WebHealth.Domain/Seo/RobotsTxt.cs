namespace WebHealth.Domain.Seo;

/// <summary>One <c>Allow</c> or <c>Disallow</c> line. The pattern is the path as authored.</summary>
public sealed record RobotsRule(bool IsAllow, string Pattern)
{
    /// <summary>
    /// Longest match wins, and the length that counts is the authored pattern's — not how much of
    /// the path it happened to consume.
    /// </summary>
    public int Specificity => Pattern.Length;
}

public sealed record RobotsGroup(IReadOnlyList<string> Agents, IReadOnlyList<RobotsRule> Rules);

public sealed record RobotsFile(IReadOnlyList<RobotsGroup> Groups, IReadOnlyList<string> Sitemaps)
{
    public static RobotsFile Empty { get; } = new([], []);

    /// <summary>A file with no groups disallows nothing, which is also what a 404 means.</summary>
    public bool IsEmpty => Groups.Count == 0;
}

/// <summary>The decision for one path, with the rule that produced it so a finding can show it.</summary>
public readonly record struct RobotsDecision(bool IsAllowed, RobotsRule? MatchedRule)
{
    public static RobotsDecision Allowed => new(true, null);
}

/// <summary>
/// A pure parser for <c>robots.txt</c> (BR-E06, BR-E07). No I/O and no clock: the text comes from
/// a stored snapshot, and every decision here is a function of that text and a path.
/// </summary>
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

        // A BOM-prefixed first line is common, and leaving it in turns "User-agent" into an
        // unknown directive — which silently drops the first group and, with it, every rule.
        content = content.TrimStart('\uFEFF');

        var groups = new List<RobotsGroup>();
        var sitemaps = new List<string>();
        var agents = new List<string>();
        var rules = new List<RobotsRule>();

        // A new group starts at the first User-agent line that follows a rule line. Consecutive
        // User-agent lines are one group with several agents, which is the whole reason this is a
        // state machine rather than a line-by-line switch.
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

                // A rule before any User-agent belongs to no group, so it is dropped rather than
                // silently attached to the first group that appears later.
                case AllowDirective when agents.Count > 0 && value.Length > 0:
                    rules.Add(new(true, value));
                    awaitingRules = true;
                    break;

                case DisallowDirective when agents.Count > 0:
                    // An empty Disallow means "nothing is disallowed": it opens the group rather
                    // than blocking it, so it is recorded as a rule that never matches.
                    if (value.Length > 0) rules.Add(new(false, value));
                    awaitingRules = true;
                    break;

                case SitemapDirective when value.Length > 0:
                    sitemaps.Add(value);
                    break;

                // Unknown directives are ignored. The file is written by someone else, and a
                // parser that failed on the common case would be useless.
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

    /// <summary>
    /// The most specific matching agent wins; <c>*</c> is a fallback used only when no named agent
    /// matches. Groups that match equally are merged, which is what the convention asks for when a
    /// file repeats an agent.
    /// </summary>
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

    /// <summary>
    /// Longest match wins; an Allow beats a Disallow of the same length. The tie is resolved
    /// towards Allow deliberately: resolving it the other way would report a site as unindexable
    /// where a crawler would happily crawl it.
    /// </summary>
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

    /// <summary>
    /// Prefix matching with <c>*</c> and <c>$</c>. Compiled to an explicit two-pointer matcher
    /// rather than to a regular expression: the pattern comes from a remote host, and handing an
    /// attacker-supplied pattern to a backtracking engine inside the monitoring worker is the trap
    /// this project already refused for HTML. This matcher cannot backtrack exponentially.
    /// </summary>
    public static bool Matches(string pattern, string path)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(path);
        if (pattern.Length == 0) return false;

        // An unanchored pattern matches a prefix of the path, which is the same as a full match
        // against the pattern with a trailing wildcard.
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
                // Paths are compared case-sensitively: URLs are.
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

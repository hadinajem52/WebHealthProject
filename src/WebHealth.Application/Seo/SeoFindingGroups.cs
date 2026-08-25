namespace WebHealth.Application.Seo;

public static class SeoFindingGroups
{
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Canonical = "Canonical";
    public const string Indexing = "Indexing";
    public const string Robots = "Robots";
    public const string Sitemap = "Sitemap";
    public const string Other = "SEO";

    private static readonly (string RuleKey, string Group)[] Membership =
    [
        (RobotsRules.BlocksSite, Robots),
        (RobotsRules.BlocksEndpoint, Robots),
        (RobotsRules.Unavailable, Robots),
        (RobotsRules.SitemapMissing, Sitemap),
        (SeoRules.TitleMissing, Title),
        (SeoRules.TitleDuplicate, Title),
        (SeoRules.DescriptionMissing, Description),
        (SeoRules.CanonicalNotAbsolute, Canonical),
        (SeoRules.CanonicalInvalid, Canonical),
        (SeoRules.CanonicalDuplicate, Canonical),
        (SeoRules.CanonicalUnexpectedHost, Canonical),
        (SeoRules.NoIndexUnexpected, Indexing),
        (SeoRules.IndexableUnexpected, Indexing),
    ];

    public static IReadOnlyList<string> SiteWide => [Robots, Sitemap];

    public static IReadOnlyList<string> Selectable =>
        [Title, Description, Canonical, Indexing, Robots, Sitemap];

    public static string Of(string? ruleKey)
    {
        foreach (var (key, group) in Membership)
        {
            if (string.Equals(key, ruleKey, StringComparison.Ordinal)) return group;
        }

        return Other;
    }

    public static IReadOnlyList<string> RuleKeysFor(string? group) =>
        [.. Membership
            .Where(entry => string.Equals(entry.Group, group, StringComparison.Ordinal))
            .Select(entry => entry.RuleKey)];

    public static bool IsSelectable(string? group) =>
        group is not null && Selectable.Contains(group, StringComparer.Ordinal);

    public static bool IsSiteWide(string? ruleKey) => SiteWide.Contains(Of(ruleKey));
}

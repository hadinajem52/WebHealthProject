namespace WebHealth.Application.Seo;

/// <summary>
/// Which part of a site's SEO configuration a rule is about.
/// <para>
/// §11.2 asks the SEO Configuration report for "title, description, canonical, indexing, robots
/// and sitemap findings" — six named subjects, not one total. A count alone cannot answer the
/// question the report exists to answer, because a robots.txt disallowing the whole origin and a
/// missing meta description are different problems with different owners and different urgency,
/// and both arrive as "Seo." rule keys.
/// </para>
/// <para>
/// The group is derived from the rule key rather than stored beside it: the key is already the
/// stable identity a finding carries everywhere, and a second stored field would be one more thing
/// that can disagree with it. Findings do carry a category in memory, but only the rule key reaches
/// the database, so this is the one place that translation happens.
/// </para>
/// </summary>
public static class SeoFindingGroups
{
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Canonical = "Canonical";
    public const string Indexing = "Indexing";
    public const string Robots = "Robots";
    public const string Sitemap = "Sitemap";
    public const string Other = "SEO";

    /// <summary>
    /// The single mapping. <see cref="Of" /> reads it forwards and <see cref="RuleKeysFor" />
    /// reads it backwards, so a rule cannot be described as one subject and filtered as another.
    /// </summary>
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

    /// <summary>
    /// The origin-level groups. These describe the site as a whole rather than the one page that
    /// happened to be checked, which is why they are worth telling apart from the page rules.
    /// </summary>
    public static IReadOnlyList<string> SiteWide => [Robots, Sitemap];

    /// <summary>
    /// The subjects a reader may filter by, in the order §11.2 names them.
    /// <para>
    /// <see cref="Other" /> is deliberately absent: it exists so a rule added later still groups
    /// sensibly, and its membership cannot be enumerated. Offering it as a filter would promise a
    /// query that has no rule keys to run against.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The rule keys belonging to a subject, for callers that must express the filter as data.
    /// The reader needs this because <see cref="Of" /> is a method: a database cannot run it, and
    /// evaluating it in memory would mean reading rows before filtering them.
    /// </summary>
    public static IReadOnlyList<string> RuleKeysFor(string? group) =>
        [.. Membership
            .Where(entry => string.Equals(entry.Group, group, StringComparison.Ordinal))
            .Select(entry => entry.RuleKey)];

    /// <summary>Whether a value names a subject that can be filtered on.</summary>
    public static bool IsSelectable(string? group) =>
        group is not null && Selectable.Contains(group, StringComparer.Ordinal);

    /// <summary>Whether the rule describes the origin rather than the individual page.</summary>
    public static bool IsSiteWide(string? ruleKey) => SiteWide.Contains(Of(ruleKey));
}

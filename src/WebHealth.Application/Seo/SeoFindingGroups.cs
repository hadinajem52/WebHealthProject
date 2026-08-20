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
/// that can disagree with it. Findings do persist a category, but only the rule key reaches the
/// database, so this is the one place that translation happens.
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
    /// The origin-level groups. These describe the site as a whole rather than the one page that
    /// happened to be checked, which is why they are worth telling apart from the page rules.
    /// </summary>
    public static IReadOnlyList<string> SiteWide => [Robots, Sitemap];

    public static string Of(string? ruleKey) => ruleKey switch
    {
        RobotsRules.SitemapMissing => Sitemap,
        RobotsRules.BlocksSite or RobotsRules.BlocksEndpoint or RobotsRules.Unavailable => Robots,
        SeoRules.TitleMissing or SeoRules.TitleDuplicate => Title,
        SeoRules.DescriptionMissing => Description,
        SeoRules.CanonicalNotAbsolute or SeoRules.CanonicalInvalid
            or SeoRules.CanonicalDuplicate or SeoRules.CanonicalUnexpectedHost => Canonical,
        SeoRules.NoIndexUnexpected or SeoRules.IndexableUnexpected => Indexing,
        _ => Other
    };

    /// <summary>Whether the rule describes the origin rather than the individual page.</summary>
    public static bool IsSiteWide(string? ruleKey) => SiteWide.Contains(Of(ruleKey));
}

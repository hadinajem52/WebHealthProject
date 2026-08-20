using WebHealth.Application.Registry;

namespace WebHealth.Application.Seo;

/// <summary>
/// The SEO view's filter. Every field is applied in the database by the reader, never by trimming a
/// list the caller already fetched: a filter applied after the fact would still have read rows the
/// requester is not entitled to see.
/// </summary>
/// <param name="Subject">
/// Narrows to endpoints whose latest observation has a finding about this part of the SEO
/// configuration — one of <see cref="SeoFindingGroups.Selectable" />. It answers "show me only the
/// robots problems", which <see cref="ProblemsOnly" /> cannot: that flag asks whether there is any
/// SEO finding at all, and every SEO rule shares one key prefix.
/// </param>
public sealed record SeoQuery(
    string? Applicability = null,
    string? Environment = null,
    bool ProblemsOnly = false,
    Guid? WebsiteId = null,
    string? Subject = null)
{
    public const int PageSize = 25;

    /// <summary>Environment filter values. Anything else is treated as no filter.</summary>
    public const string Production = "Production";
    public const string NonProduction = "NonProduction";
}

/// <summary>
/// One endpoint's most recent SEO decision. Values only, with their observed lengths — the document
/// they came from is never stored and so can never be shown (BR-E10).
/// </summary>
public sealed record SeoListItem(
    Guid EndpointId,
    Guid LogicalCheckId,
    string EndpointUrl,
    string WebsiteName,
    string EnvironmentName,
    bool IsProduction,
    string Applicability,
    string? NotApplicableReason,
    bool DocumentTruncated,
    string? Title,
    int TitleLength,
    int TitleCount,
    string? MetaDescription,
    int MetaDescriptionLength,
    string? CanonicalAbsoluteUrl,
    int CanonicalCount,
    string? RobotsMeta,
    string PolicyIndexingExpectation,
    IReadOnlyList<string> FindingRuleKeys,
    DateTimeOffset ObservedAt)
{
    /// <summary>
    /// How many SEO rules this observation breaks. Kept as a derived value so the count and the
    /// findings behind it cannot disagree.
    /// </summary>
    public int OpenFindingCount => FindingRuleKeys.Count;

    /// <summary>
    /// The findings grouped by what they are about, in a stable order, so the report can say
    /// "robots" or "canonical" rather than only "3" (§11.2).
    /// </summary>
    public IReadOnlyList<SeoFindingGroupCount> FindingGroups =>
        [.. FindingRuleKeys
            .GroupBy(SeoFindingGroups.Of)
            .Select(group => new SeoFindingGroupCount(group.Key, group.Count()))
            .OrderByDescending(group => SeoFindingGroups.SiteWide.Contains(group.Group))
            .ThenBy(group => group.Group, StringComparer.Ordinal)];

    /// <summary>
    /// Whether the page is asking search engines to stay away, read from the directives the
    /// extractor already combined. Presented as a fact rather than as a judgement: whether it is
    /// *wrong* depends on the environment's expectation, which is the finding's job to say.
    /// </summary>
    public bool DeclaresNoIndex => SeoRuleEvaluator.IsNoIndex(RobotsMeta);
}

/// <summary>How many findings one subject of the SEO configuration has (§11.2).</summary>
public sealed record SeoFindingGroupCount(string Group, int Count);

public sealed record SeoListPage(IReadOnlyList<SeoListItem> Items, int Page, int PageSize, int TotalCount);

public interface ISeoReader
{
    /// <summary>
    /// The latest SEO observation per endpoint the requester may see. Access scoping is the
    /// reader's responsibility, exactly as it is for incidents: a view that filtered afterwards
    /// would already have read another client's data.
    /// </summary>
    Task<SeoListPage> ListAsync(
        SeoQuery query,
        RegistryAccessContext access,
        int page,
        CancellationToken cancellationToken = default);
}

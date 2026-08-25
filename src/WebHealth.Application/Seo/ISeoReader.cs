using WebHealth.Application.Registry;

namespace WebHealth.Application.Seo;

public sealed record SeoQuery(
    string? Applicability = null,
    string? Environment = null,
    bool ProblemsOnly = false,
    Guid? WebsiteId = null,
    string? Subject = null)
{
    public const int PageSize = 25;

    public const string Production = "Production";
    public const string NonProduction = "NonProduction";
}

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
    public int OpenFindingCount => FindingRuleKeys.Count;

    public IReadOnlyList<SeoFindingGroupCount> FindingGroups =>
        [.. FindingRuleKeys
            .GroupBy(SeoFindingGroups.Of)
            .Select(group => new SeoFindingGroupCount(group.Key, group.Count()))
            .OrderByDescending(group => SeoFindingGroups.SiteWide.Contains(group.Group))
            .ThenBy(group => group.Group, StringComparer.Ordinal)];

    public bool DeclaresNoIndex => SeoRuleEvaluator.IsNoIndex(RobotsMeta);
}

public sealed record SeoFindingGroupCount(string Group, int Count);

public sealed record SeoListPage(IReadOnlyList<SeoListItem> Items, int Page, int PageSize, int TotalCount);

public interface ISeoReader
{
    Task<SeoListPage> ListAsync(
        SeoQuery query,
        RegistryAccessContext access,
        int page,
        CancellationToken cancellationToken = default);
}

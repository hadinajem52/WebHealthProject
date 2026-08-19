using WebHealth.Application.Registry;

namespace WebHealth.Application.Seo;

/// <summary>
/// The SEO view's filter. Every field is applied in the database by the reader, never by trimming a
/// list the caller already fetched: a filter applied after the fact would still have read rows the
/// requester is not entitled to see.
/// </summary>
public sealed record SeoQuery(
    string? Applicability = null,
    string? Environment = null,
    bool ProblemsOnly = false,
    Guid? WebsiteId = null)
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
    int OpenFindingCount,
    DateTimeOffset ObservedAt)
{
    /// <summary>
    /// Whether the page is asking search engines to stay away, read from the directives the
    /// extractor already combined. Presented as a fact rather than as a judgement: whether it is
    /// *wrong* depends on the environment's expectation, which is the finding's job to say.
    /// </summary>
    public bool DeclaresNoIndex =>
        RobotsMeta is not null && RobotsMeta.Contains("noindex", StringComparison.OrdinalIgnoreCase);
}

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

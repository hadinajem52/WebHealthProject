using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Application.PageAudits;

/// <summary>
/// One run, as a page reads it. The display score is derived from the raw one here rather than
/// stored, so the number shown and the number compared can never drift apart.
/// </summary>
public sealed record PageAuditRunSummary(
    Guid RunId,
    Guid EndpointId,
    string Source,
    string Status,
    string RequestedUrl,
    string? FinalUrl,
    decimal? RawScore,
    string Strategy,
    string Locale,
    string? LighthouseVersion,
    string? WarningSummary,
    string? FailureCategory,
    string? SafeDiagnostic,
    int AttemptCount,
    DateTimeOffset QueuedAt,
    DateTimeOffset? AnalysisAt,
    DateTimeOffset? FinishedAt)
{
    public int? Score => PageAuditNormalization.ToDisplayScore(RawScore);

    public bool IsActive => PageAuditRunStatuses.IsActive(Status);

    /// <summary>
    /// Whether this run produced a score worth reading. A run with failing audits qualifies; a run
    /// that never reached the provider does not, and the two must not render alike.
    /// </summary>
    public bool HasScore => PageAuditRunStatuses.IsScored(Status) && RawScore is not null;

    /// <summary>The redirect the provider followed, or null when it audited what we asked for.</summary>
    public string? RedirectedTo =>
        FinalUrl is not null && !string.Equals(FinalUrl, RequestedUrl, StringComparison.Ordinal)
            ? FinalUrl
            : null;
}

/// <summary>
/// How many audits of each status one run recorded. Manual and not-applicable are carried
/// separately from passed, because a page cannot be given credit for a check nobody ran.
/// </summary>
public sealed record PageAuditItemCounts(
    int Failed,
    int Passed,
    int Scored,
    int Manual,
    int NotApplicable,
    int Informative,
    int Error)
{
    public static PageAuditItemCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public int Total => Failed + Passed + Scored + Manual + NotApplicable + Informative + Error;
}

/// <summary>One normalized audit, ready to render. Provider text only, bounded at write time.</summary>
public sealed record PageAuditItemView(
    string AuditId,
    string Status,
    decimal? Score,
    string? ScoreDisplayMode,
    double Weight,
    string? GroupName,
    string? Title,
    string? Description,
    string? DisplayValue,
    string? Explanation,
    string? ErrorMessage);

/// <summary>
/// How the current score compares with the one before it.
/// </summary>
/// <param name="Comparability">
/// <c>LighthouseVersionChanged</c> when the two runs used different Lighthouse major versions. The
/// delta is still shown, and still labelled: a major version can add, remove or redefine audits,
/// so the number is a change in measurement as much as a change in the page.
/// </param>
public sealed record PageAuditComparison(
    Guid? CurrentRunId,
    Guid? PreviousRunId,
    int? CurrentScore,
    int? PreviousScore,
    string? Comparability)
{
    public static PageAuditComparison None { get; } = new(null, null, null, null, null);

    public int? Delta => CurrentScore is { } current && PreviousScore is { } previous
        ? current - previous
        : null;

    public bool SpansAVersionChange =>
        Comparability == PageAuditComparability.LighthouseVersionChanged;
}

/// <summary>
/// One endpoint's page-audit state: how it is configured, its latest run, what that run found, and
/// how it compares with the run before it.
/// </summary>
public sealed record PageAuditEndpointSummary(
    Guid EndpointId,
    string EndpointUrl,
    string WebsiteName,
    string EnvironmentName,
    bool IsConfigured,
    bool IsEnabled,
    bool SchedulingEnabled,
    string Strategy,
    int IntervalHours,
    DateTimeOffset? NextDueAt,
    PageAuditRunSummary? LatestRun,
    PageAuditItemCounts Counts,
    PageAuditComparison Comparison)
{
    public static PageAuditEndpointSummary NotConfigured(
        Guid endpointId,
        string endpointUrl,
        string websiteName,
        string environmentName) =>
        new(endpointId, endpointUrl, websiteName, environmentName, false, false, false,
            PageAuditStrategies.Mobile, 24, null, null, PageAuditItemCounts.Empty,
            PageAuditComparison.None);
}

/// <summary>
/// The page-audit read surface. Every method takes the requester's access context and scopes to
/// endpoints they may see, in the database. A reader that trusted a caller-supplied endpoint id
/// would let any authenticated user read another client's audit history by guessing one — the id
/// is a parameter, not a permission.
/// </summary>
public interface IPageAuditReader
{
    Task<PageAuditEndpointSummary?> GetEndpointSummaryAsync(
        Guid endpointId,
        Guid? runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageAuditRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One run's audits. Bounded by the reader rather than by the caller: a run holds a couple of
    /// dozen SEO audits today, and the bound is what keeps that true if a category grows.
    /// </summary>
    Task<IReadOnlyList<PageAuditItemView>> ListAuditItemsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

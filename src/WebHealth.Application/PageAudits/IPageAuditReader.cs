using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Application.PageAudits;

public sealed record PageAuditRunSummary(
    Guid RunId,
    Guid BatchId,
    Guid EndpointId,
    string Source,
    string Status,
    string RequestedUrl,
    string? FinalUrl,
    decimal? RawScore,
    string Category,
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

    public bool HasScore => PageAuditRunStatuses.IsScored(Status) && RawScore is not null;

    public string? RedirectedTo =>
        FinalUrl is not null && !string.Equals(FinalUrl, RequestedUrl, StringComparison.Ordinal)
            ? FinalUrl
            : null;
}

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

public sealed record PageAuditItemView(
    string AuditId,
    string Status,
    decimal? Score,
    string? ScoreDisplayMode,
    decimal? NumericValue,
    string? NumericUnit,
    double Weight,
    string? GroupName,
    string? Title,
    string? Description,
    string? DisplayValue,
    string? Explanation,
    string? ErrorMessage);

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

public sealed record PageAuditEndpointSummary(
    Guid EndpointId,
    string EndpointUrl,
    string WebsiteName,
    string EnvironmentName,
    bool IsConfigured,
    bool IsEnabled,
    bool SchedulingEnabled,
    string Category,
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
        string environmentName,
        string category,
        string strategy) =>
        new(endpointId, endpointUrl, websiteName, environmentName, false, false, false,
            category, strategy, 24, null, null, PageAuditItemCounts.Empty,
            PageAuditComparison.None);
}

public sealed record PageAuditCategorySummary(
    string Category,
    PageAuditRunSummary? LatestRun);

public interface IPageAuditReader
{
    Task<PageAuditEndpointSummary?> GetEndpointSummaryAsync(
        Guid endpointId,
        string category,
        string strategy,
        Guid? runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageAuditCategorySummary>?> GetLatestCategorySummariesAsync(
        Guid endpointId,
        string strategy,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageAuditRunSummary>> ListRunsAsync(
        Guid endpointId,
        string category,
        string strategy,
        int limit,
        RegistryAccessContext access,
        bool archivedOnly = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageAuditItemView>> ListAuditItemsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;

namespace WebHealth.Application.Crawling;

public sealed record CrawlRunRequest(
    Guid RunId,
    Guid EndpointId,
    bool IsProduction,
    IReadOnlyList<string> SeedUrls)
{
    public IReadOnlyList<CrawlHostRule>? AllowedHosts { get; init; }

    public IReadOnlyList<string>? AllowedPathPrefixes { get; init; }

    public CrawlLimits Limits { get; init; } = CrawlLimits.Default;

    public CrawlUrlOptions UrlOptions { get; init; } = CrawlUrlOptions.Default;

    public bool CheckExternalLinks { get; init; }

    public bool RequestRobotsOverride { get; init; }
}

public sealed record CrawlRunOutcome(
    Guid RunId,
    string Status,
    string StopReason,
    int PagesFetched,
    int LinksRecorded,
    bool RobotsOverrideGranted,
    string? RobotsOverrideRefusedBecause,
    IReadOnlyList<string> ValidationErrors)
{
    public string? FailureCode { get; init; }

    public bool CoverageLimited { get; init; }

    public static CrawlRunOutcome Invalid(Guid runId, IReadOnlyList<string> errors) => new(
        runId, CrawlRunStatuses.Failed, CrawlStopReasons.Failed, 0, 0, false, null, errors);
}

public sealed record CrawlLinkRecord(
    Guid RunId,
    string? SourceUrl,
    string TargetUrl,
    bool IsInternal,
    int Depth,
    string Classification,
    int? StatusCode,
    int RedirectCount,
    string? FinalUrl,
    string? SkipReason,
    int? DurationMs)
{
    public string? SourceUrlIdentity { get; init; }

    public string? TargetUrlIdentity { get; init; }
}

public sealed record CrawlRunStart(
    Guid RunId,
    Guid EndpointId,
    IReadOnlyList<string> SeedUrls,
    CrawlRunSettings Settings,
    DateTimeOffset StartedAt);

public sealed record CrawlRunSettings(
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<string> AllowedPathPrefixes,
    string QueryPolicy,
    int MaxPages,
    int MaxDepth,
    bool CheckExternalLinks)
{
    public IReadOnlySet<string> SensitiveQueryParameters { get; init; } =
        CrawlUrlOptions.DefaultSensitiveQueryParameters;

    public static CrawlRunSettings From(CrawlRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            [.. (request.AllowedHosts ?? []).Select(rule =>
                rule.IncludeSubdomains ? $"*.{rule.Host}" : rule.Host)],
            [.. request.AllowedPathPrefixes ?? []],
            request.UrlOptions.QueryPolicy.ToString(),
            request.Limits.MaxPages,
            request.Limits.MaxDepth,
            request.CheckExternalLinks)
        {
            SensitiveQueryParameters = request.UrlOptions.SensitiveQueryParameters
        };
    }
}

public interface ICrawlResultSink
{
    Task BeginRunAsync(CrawlRunStart start, CancellationToken cancellationToken = default);

    Task<bool> TryClaimRunAsync(
        Guid runId,
        Guid executionClaimId,
        CancellationToken cancellationToken = default);

    Task RecordLinkAsync(CrawlLinkRecord record, CancellationToken cancellationToken = default);

    Task<int> RecordLinksAsync(
        IReadOnlyList<CrawlLinkRecord> records,
        CancellationToken cancellationToken = default);

    Task<bool> RecordRunOutcomeAsync(
        CrawlRunOutcome outcome,
        Guid executionClaimId,
        CancellationToken cancellationToken = default);
}

public interface ICrawlExecutionService
{
    Task<CrawlRunOutcome?> ExecuteAsync(
        CrawlRunRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICrawlRobotsReader
{
    Task<CrawlRobotsFacts> GetAsync(string origin, CancellationToken cancellationToken = default);
}

public sealed record CrawlDocumentLinks(
    IReadOnlyList<string> Hrefs,
    bool FullyInspected,
    string? BaseHref = null)
{
    public static CrawlDocumentLinks NotInspected { get; } = new([], false);

    public static CrawlDocumentLinks Nothing { get; } = new([], true);
}

public interface IHtmlLinkExtractor
{
    CrawlDocumentLinks ExtractHrefs(ReadOnlyMemory<byte> body, string? contentType);
}

public sealed record CrawlRunSummary(
    Guid RunId,
    Guid EndpointId,
    string Status,
    string StopReason,
    int PagesFetched,
    int LinksRecorded,
    int BrokenLinkCount,
    bool RobotsOverrideGranted,
    string? RobotsOverrideRefusedBecause,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? FailureReason = null,
    bool CoverageLimited = false)
{
    public bool CoveredWholeScope =>
        Status == Domain.Crawling.CrawlRunStatuses.Completed
        && StopReason == Domain.Crawling.CrawlStopReasons.FrontierExhausted
        && PagesFetched > 0
        && !CoverageLimited;
}

public sealed record CrawlSkipSummary(string SkipReason, int Count);

public sealed record CrawlBrokenLink(
    string? SourceUrl,
    string TargetUrl,
    string Classification,
    int? StatusCode,
    bool IsInternal);

public sealed record CrawlComparisonBucket(int TotalCount, IReadOnlyList<CrawlBrokenLink> Sample)
{
    public static CrawlComparisonBucket Empty { get; } = new(0, []);

    public bool HasMore => TotalCount > Sample.Count;
}

public sealed record CrawlComparison(
    Guid? CurrentRunId,
    Guid? PreviousRunId,
    CrawlComparisonBucket New,
    CrawlComparisonBucket Continuing,
    CrawlComparisonBucket Resolved,
    CrawlComparisonBucket Indeterminate)
{
    public static CrawlComparison Empty { get; } = new(
        null, null,
        CrawlComparisonBucket.Empty,
        CrawlComparisonBucket.Empty,
        CrawlComparisonBucket.Empty,
        CrawlComparisonBucket.Empty);
}

public interface ICrawlReportReader
{
    Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        int limit,
        RegistryAccessContext access,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CrawlSkipSummary>> ListSkipReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<CrawlRunSummary?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public sealed record CrawlManualResult(
    Guid? RunId,
    bool WasAlreadyRunning,
    string? Error,
    EndpointTestBlock Block = EndpointTestBlock.None)
{
    public bool Succeeded => RunId is not null;

    public static CrawlManualResult Queued(Guid runId) => new(runId, false, null);

    public static CrawlManualResult AlreadyRunning(Guid runId) => new(runId, true, null);

    public static CrawlManualResult Rejected(string error) => new(null, false, error);

    public static CrawlManualResult NotTestable(EndpointTestBlock block) => new(null, false, null, block);
}

public interface ICrawlRunner
{
    bool CanQueue { get; }

    Task<CrawlManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        bool checkExternalLinks,
        CancellationToken cancellationToken = default);
}

public interface ICrawlReconciler
{
    Task<int> RetireAbandonedRunsAsync(CancellationToken cancellationToken = default);

    Task<int> RetireAbandonedRunsAsync(Guid endpointId, CancellationToken cancellationToken = default);
}

public interface ICrawlRunQueue
{
    void Enqueue(Guid runId);
}

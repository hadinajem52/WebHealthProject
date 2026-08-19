using WebHealth.Domain.Crawling;

namespace WebHealth.Application.Crawling;

/// <summary>
/// One crawl to run. The endpoint carries the target-authorization evidence every request is
/// checked against, and its environment decides whether a robots override can be granted at all.
/// </summary>
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

    /// <summary>
    /// BR-L08 is permission, not obligation. External checking stays off by default because every
    /// external fetch still needs its own target-authorization evidence, and a run that silently
    /// skipped most of them would report less than it appears to.
    /// </summary>
    public bool CheckExternalLinks { get; init; }

    /// <summary>BR-L02. Granted only for a non-production target with an approved exception.</summary>
    public bool RequestRobotsOverride { get; init; }
}

/// <summary>
/// How a run ended. <c>Status</c> and <c>StopReason</c> are separate because "it stopped" and "it
/// covered the site" are different facts, and only <c>FrontierExhausted</c> means the second.
/// </summary>
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
    public static CrawlRunOutcome Invalid(Guid runId, IReadOnlyList<string> errors) => new(
        runId, CrawlRunStatuses.Failed, CrawlStopReasons.Failed, 0, 0, false, null, errors);
}

/// <summary>One recorded source-target result, ready for the sink 6.7 implements.</summary>
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
    string? SkipReason);

/// <summary>
/// What a run is, before it has produced anything. Opening the run first means results always have
/// something to belong to, and an interrupted process leaves a visibly unfinished run rather than
/// no trace that a crawl was ever asked for.
/// </summary>
public sealed record CrawlRunStart(
    Guid RunId,
    Guid EndpointId,
    IReadOnlyList<string> SeedUrls,
    DateTimeOffset StartedAt);

/// <summary>
/// Where results go as they resolve. Writing per result rather than batching at the end is what
/// makes BR-L10 need no special cancellation path: whatever was found is already recorded.
/// </summary>
public interface ICrawlResultSink
{
    Task BeginRunAsync(CrawlRunStart start, CancellationToken cancellationToken = default);

    Task RecordLinkAsync(CrawlLinkRecord record, CancellationToken cancellationToken = default);

    Task RecordRunOutcomeAsync(CrawlRunOutcome outcome, CancellationToken cancellationToken = default);
}

public interface ICrawlExecutionService
{
    Task<CrawlRunOutcome> ExecuteAsync(
        CrawlRunRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads the per-origin robots snapshot 6.4 maintains. The crawl never fetches one.</summary>
public interface ICrawlRobotsReader
{
    Task<CrawlRobotsFacts> GetAsync(string origin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extracts <c>href</c> values from a document and returns nothing else. The narrow return type is
/// the point: BR-E10 stays structural rather than a convention if the document has no way out.
/// </summary>
public interface IHtmlLinkExtractor
{
    IReadOnlyList<string> ExtractHrefs(ReadOnlyMemory<byte> body, string? contentType);
}

/// <summary>One run, as a report row. No link detail: the views page that separately.</summary>
public sealed record CrawlRunSummary(
    Guid RunId,
    Guid EndpointId,
    string Status,
    string StopReason,
    int PagesFetched,
    int LinksRecorded,
    int BrokenLinkCount,
    bool RobotsOverrideGranted,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt)
{
    /// <summary>
    /// Only an exhausted frontier means the site was covered. The views must never render a run
    /// that stopped on a budget as a clean result, so the distinction is carried, not inferred.
    /// </summary>
    public bool CoveredWholeScope =>
        Status == Domain.Crawling.CrawlRunStatuses.Completed
        && StopReason == Domain.Crawling.CrawlStopReasons.FrontierExhausted;
}

/// <summary>One broken source-target pair, which is what a report is actually for (AC-08).</summary>
public sealed record CrawlBrokenLink(
    string? SourceUrl,
    string TargetUrl,
    string Classification,
    int? StatusCode,
    bool IsInternal);

/// <summary>
/// Two runs of the same endpoint, bucketed. <c>PreviousRunId</c> is null for a first crawl, where
/// every broken link is new because there is nothing to compare against — which is different from
/// a run that genuinely introduced them, and is why the null is carried rather than hidden.
/// </summary>
public sealed record CrawlComparison(
    Guid? CurrentRunId,
    Guid? PreviousRunId,
    IReadOnlyList<CrawlBrokenLink> New,
    IReadOnlyList<CrawlBrokenLink> Continuing,
    IReadOnlyList<CrawlBrokenLink> Resolved)
{
    public static CrawlComparison Empty { get; } = new(null, null, [], [], []);
}

public interface ICrawlReportReader
{
    Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the latest completed run for the endpoint against the completed run before it.
    /// A cancelled run is never the current side: it covered only part of the site, so every link
    /// it did not reach would surface as resolved and a partial crawl would manufacture good news.
    /// </summary>
    Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default);
}

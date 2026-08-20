using WebHealth.Application.Registry;
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
    string? SkipReason,
    int? DurationMs);

/// <summary>
/// What a run is, before it has produced anything. Opening the run first means results always have
/// something to belong to, and an interrupted process leaves a visibly unfinished run rather than
/// no trace that a crawl was ever asked for.
/// </summary>
public sealed record CrawlRunStart(
    Guid RunId,
    Guid EndpointId,
    IReadOnlyList<string> SeedUrls,
    CrawlRunSettings Settings,
    DateTimeOffset StartedAt);

/// <summary>
/// The configuration a run was launched with, stored beside its results. Without it a result set
/// cannot be explained later: "no broken links past depth three" means nothing unless the depth
/// limit that produced it is recorded, and a limit edited afterwards would silently rewrite the
/// meaning of stored history — the same reason <c>seo_observation</c> stores the policy it was
/// judged against.
/// <para>
/// Empty host and prefix lists mean "derived from the seeds", which is what the run was actually
/// configured with rather than a value invented at write time.
/// </para>
/// </summary>
public sealed record CrawlRunSettings(
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<string> AllowedPathPrefixes,
    string QueryPolicy,
    int MaxPages,
    int MaxDepth,
    bool CheckExternalLinks)
{
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
            request.CheckExternalLinks);
    }
}

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
    /// Covered means the crawler actually examined the site: the frontier drained <em>and</em> at
    /// least one page was fetched. The views must never render a run that stopped on a budget as a
    /// clean result, so the distinction is carried, not inferred.
    /// <para>
    /// The page count is part of the test because an exhausted frontier is not evidence on its own.
    /// A run refused at every door — robots disallowing the origin, every target unauthorized —
    /// drains its frontier too, having looked at nothing. Treating that as full coverage lets it
    /// stand as the baseline a later comparison is drawn against, and every previously broken link
    /// then surfaces as resolved on the strength of a crawl that checked nothing.
    /// </para>
    /// </summary>
    public bool CoveredWholeScope =>
        Status == Domain.Crawling.CrawlRunStatuses.Completed
        && StopReason == Domain.Crawling.CrawlStopReasons.FrontierExhausted
        && PagesFetched > 0;
}

/// <summary>One broken source-target pair, which is what a report is actually for (AC-08).</summary>
public sealed record CrawlBrokenLink(
    string? SourceUrl,
    string TargetUrl,
    string Classification,
    int? StatusCode,
    bool IsInternal);

/// <summary>
/// One bucket of a comparison: how many links fall in it, and a bounded sample to show.
/// <para>
/// The count comes from the database and is exact; the sample is capped. A page cannot render an
/// unbounded number of rows, and truncating the *set* instead of the *display* would be worse than
/// slow — a previous run whose links were cut short would report them as resolved.
/// </para>
/// </summary>
public sealed record CrawlComparisonBucket(int TotalCount, IReadOnlyList<CrawlBrokenLink> Sample)
{
    public static CrawlComparisonBucket Empty { get; } = new(0, []);

    public bool HasMore => TotalCount > Sample.Count;
}

/// <summary>
/// Two runs of the same endpoint, bucketed. <c>PreviousRunId</c> is null for a first crawl, where
/// every broken link is new because there is nothing to compare against — which is different from
/// a run that genuinely introduced them, and is why the null is carried rather than hidden.
/// </summary>
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

/// <summary>
/// AC-08's read surface. Every method takes the requester's access context and scopes to endpoints
/// they may see, in the database. A reader that trusted a caller-supplied endpoint id would let any
/// authenticated user read another client's crawl results by guessing one — the id is a parameter,
/// not a permission.
/// </summary>
public interface ICrawlReportReader
{
    Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One bounded page of a run's broken links. A run may carry a thousand pages' worth of them,
    /// so the bound is on the reader rather than left to the caller: an unbounded read here would
    /// be a query the views could not safely issue.
    /// </summary>
    Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        int limit,
        RegistryAccessContext access,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the latest **full-scope** run for the endpoint against the full-scope run before it.
    /// A run that stopped on any budget is never used: it covered only part of the site, so every
    /// link it did not reach would surface as resolved and a partial crawl would manufacture good
    /// news.
    /// </summary>
    Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>One run's summary, or null when the requester may not see its endpoint.</summary>
    Task<CrawlRunSummary?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

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
    /// <summary>
    /// What went wrong, when the run failed on an exception rather than on invalid configuration.
    /// A failed run whose only account of itself is "it failed" leaves the reader with nowhere to
    /// go, and the exception is otherwise lost the moment the run is summarised.
    /// </summary>
    public string? FailureDetail { get; init; }

    /// <summary>
    /// Whether anything left part of the site unexamined: a page nobody could read, a robots rule,
    /// a budget. A run carrying this has not covered its scope, whatever its stop reason says, and
    /// must not stand as the baseline a later comparison calls links resolved against.
    /// </summary>
    public bool CoverageLimited { get; init; }

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
/// What one document yielded, and whether that is all of it.
/// <para>
/// "This page links to nothing" and "this page could not be read" produce the same empty list and
/// mean opposite things. A comparison that cannot tell them apart reports every link the
/// unreadable page used to carry as resolved, on the strength of nobody having looked.
/// </para>
/// </summary>
public sealed record CrawlDocumentLinks(IReadOnlyList<string> Hrefs, bool FullyInspected)
{
    /// <summary>A document whose links were never enumerated.</summary>
    public static CrawlDocumentLinks NotInspected { get; } = new([], false);

    /// <summary>A document with nothing to enumerate: no markup this crawler reads links out of.</summary>
    public static CrawlDocumentLinks Nothing { get; } = new([], true);
}

/// <summary>
/// Extracts <c>href</c> values from a document and returns nothing else beyond whether it read the
/// whole document. The narrow return type is the point: BR-E10 stays structural rather than a
/// convention if the document has no way out.
/// </summary>
public interface IHtmlLinkExtractor
{
    CrawlDocumentLinks ExtractHrefs(ReadOnlyMemory<byte> body, string? contentType);
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
    string? RobotsOverrideRefusedBecause,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? FailureReason = null,
    bool CoverageLimited = false)
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
        && PagesFetched > 0
        && !CoverageLimited;
}

/// <summary>
/// How many of a run's URLs went unrequested for one reason. A crawl that fetched nothing has to be
/// able to say what stopped it: "nothing was examined" with no reason behind it leaves the one
/// person who has to act on it with nowhere to go.
/// </summary>
public sealed record CrawlSkipSummary(string SkipReason, int Count);

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
    /// Why this run's URLs were not requested, most common reason first. Bounded by the number of
    /// distinct reasons, which is a fixed, small vocabulary rather than a function of crawl size.
    /// </summary>
    Task<IReadOnlyList<CrawlSkipSummary>> ListSkipReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
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

/// <summary>
/// What happened to a crawl somebody asked for by hand. An existing run is a distinct answer from
/// a new one, so the page can say "already running" rather than implying it started something.
/// </summary>
public sealed record CrawlManualResult(Guid? RunId, bool WasAlreadyRunning, string? Error)
{
    public bool Succeeded => RunId is not null;

    public static CrawlManualResult Queued(Guid runId) => new(runId, false, null);

    public static CrawlManualResult AlreadyRunning(Guid runId) => new(runId, true, null);

    public static CrawlManualResult Rejected(string error) => new(null, false, error);
}

/// <summary>
/// Opening a crawl on request. This is the only door to starting one: nothing else in the
/// application enqueues <c>CrawlRunJob</c>, so every crawl this system performs against a site it
/// does not own passes through the authorization and eligibility checks behind this method.
/// </summary>
public interface ICrawlRunner
{
    /// <summary>
    /// Whether this instance can run a crawl at all. False when crawl scheduling is switched off:
    /// there is then no worker serving the crawl queue, so a run opened here would sit unstarted
    /// and hold its endpoint's only active-crawl slot. Pages use it to hide a control that could
    /// only be refused.
    /// </summary>
    bool CanQueue { get; }

    /// <summary>
    /// Opens a crawl on request, refusing an endpoint the requester may not test.
    /// </summary>
    /// <remarks>
    /// The access context is a parameter because the check belongs here rather than only in the
    /// controller that happens to call it today. An endpoint id is a parameter, not a permission.
    /// </remarks>
    Task<CrawlManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The seam between opening a crawl and the background worker that performs it. Keeping it an
/// interface is what lets the runner live in one place while Hangfire stays an infrastructure
/// detail, and lets a test observe that a run was enqueued without a job server.
/// </summary>
public interface ICrawlRunQueue
{
    void Enqueue(
        Guid runId,
        Guid endpointId,
        bool isProduction,
        IReadOnlyList<string> seedUrls,
        bool checkExternalLinks,
        bool requestRobotsOverride);
}

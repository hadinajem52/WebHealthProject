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
/// Where results go as they resolve. Writing per result rather than batching at the end is what
/// makes BR-L10 need no special cancellation path: whatever was found is already recorded.
/// </summary>
public interface ICrawlResultSink
{
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

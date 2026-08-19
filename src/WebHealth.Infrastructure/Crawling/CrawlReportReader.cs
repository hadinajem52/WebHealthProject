using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// Reads crawl runs and compares them. Every filter is a predicate on
/// <c>crawl_link_result</c> itself — <c>run_id</c> and <c>classification</c> are columns on the
/// high-volume row, so <c>ix_crawl_link_result_run_classification</c> serves them without a join
/// back to the run. That is the Phase 5 lesson applied rather than restated.
/// </summary>
internal sealed class CrawlReportReader(ApplicationDbContext dbContext) : ICrawlReportReader
{
    /// <summary>A run listing is a page of history, never the whole table.</summary>
    public const int MaxRunsListed = 100;

    public async Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.CrawlRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Take(Math.Clamp(limit, 1, MaxRunsListed))
            .Select(run => new CrawlRunSummary(
                run.Id,
                run.EndpointId,
                run.Status,
                run.StopReason,
                run.PagesFetched,
                run.LinksRecorded,
                run.Links.Count(link => link.Classification == CrawlLinkClassifications.Broken),
                run.RobotsOverrideGranted,
                run.StartedAt,
                run.FinishedAt))
            .ToArrayAsync(cancellationToken);

    /// <summary>A page of a run's broken links. The bound is the reader's, not the caller's.</summary>
    public const int MaxBrokenLinksListed = 500;

    public async Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        int limit,
        int offset = 0,
        CancellationToken cancellationToken = default) =>
        await BrokenLinksQuery(runId)
            .OrderBy(link => link.TargetUrl).ThenBy(link => link.SourceUrl)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, MaxBrokenLinksListed))
            .ToArrayAsync(cancellationToken);

    public async Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        // Only **full-scope** runs take part. A run that stopped on any budget — cancelled, page
        // limit, duration limit — covered part of the site, so every link it never reached would
        // surface as resolved. A partial crawl manufacturing good news is the one failure this
        // comparison must not have, and a page limit produces it just as readily as a cancellation.
        var runs = await dbContext.CrawlRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId
                && run.Status == CrawlRunStatuses.Completed
                && run.StopReason == CrawlStopReasons.FrontierExhausted)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => run.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        if (runs.Length == 0) return CrawlComparison.Empty;

        var current = await BrokenLinksQuery(runs[0]).ToArrayAsync(cancellationToken);
        if (runs.Length == 1)
        {
            // A first crawl has nothing to compare against. Every broken link is reported as new,
            // and the null previous run is what lets a reader tell that from a run that introduced
            // them.
            return new(runs[0], null, Ordered(current), [], [], []);
        }

        var previous = await BrokenLinksQuery(runs[1]).ToArrayAsync(cancellationToken);
        var previousPairs = previous.Select(Pair).ToHashSet(StringComparer.Ordinal);
        var currentPairs = current.Select(Pair).ToHashSet(StringComparer.Ordinal);

        // A previously broken link that is no longer broken is only resolved if the current run
        // actually established that. A timeout, a block, a skip or an unreached target says nothing
        // about whether the link works — treating those as resolved would close findings on the
        // strength of evidence the crawl never gathered.
        var unproven = (await dbContext.CrawlLinkResults.AsNoTracking()
            .Where(link => link.RunId == runs[0] && IndeterminateClassifications.Contains(link.Classification))
            .Select(link => new { link.SourceUrl, link.TargetUrl })
            .ToArrayAsync(cancellationToken))
            .Select(link => $"{link.SourceUrl}\n{link.TargetUrl}")
            .ToHashSet(StringComparer.Ordinal);

        // Resolved still folds two cases together on purpose: the pair was checked and is healthy
        // now, and the source page stopped linking to it. Both are resolutions of that broken link,
        // and separating them would report a removed link as still outstanding.
        var noLongerBroken = previous.Where(link => !currentPairs.Contains(Pair(link))).ToArray();

        return new(
            runs[0],
            runs[1],
            Ordered(current.Where(link => !previousPairs.Contains(Pair(link)))),
            Ordered(current.Where(link => previousPairs.Contains(Pair(link)))),
            Ordered(noLongerBroken.Where(link => !unproven.Contains(Pair(link)))),
            Ordered(noLongerBroken.Where(link => unproven.Contains(Pair(link)))));
    }

    /// <summary>
    /// Classifications that leave a link's health unknown. A previously broken link in one of these
    /// states has not been shown to work, so it is reported as indeterminate rather than resolved.
    /// </summary>
    private static readonly string[] IndeterminateClassifications =
    [
        CrawlLinkClassifications.Timeout,
        CrawlLinkClassifications.Blocked,
        CrawlLinkClassifications.Skipped,
        CrawlLinkClassifications.Unknown
    ];

    private IQueryable<CrawlBrokenLink> BrokenLinksQuery(Guid runId) =>
        dbContext.CrawlLinkResults.AsNoTracking()
            .Where(link => link.RunId == runId
                && link.Classification == CrawlLinkClassifications.Broken)
            .Select(link => new CrawlBrokenLink(
                link.SourceUrl, link.TargetUrl, link.Classification, link.StatusCode, link.IsInternal));

    /// <summary>
    /// The source-target pair, which is the identity BR-L07 deduplicates on. The separator is a
    /// newline because a canonical crawl URL cannot contain one.
    /// </summary>
    private static string Pair(CrawlBrokenLink link) => $"{link.SourceUrl}\n{link.TargetUrl}";

    private static IReadOnlyList<CrawlBrokenLink> Ordered(IEnumerable<CrawlBrokenLink> links) =>
        [.. links.OrderBy(link => link.TargetUrl, StringComparer.Ordinal)
            .ThenBy(link => link.SourceUrl, StringComparer.Ordinal)];
}

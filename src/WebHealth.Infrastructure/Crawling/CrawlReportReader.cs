using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// Reads crawl runs and compares them. Every filter is a predicate on
/// <c>crawl_link_result</c> itself — <c>run_id</c> and <c>classification</c> are columns on the
/// high-volume row, so <c>ix_crawl_link_result_run_classification</c> serves them without a join
/// back to the run. That is the Phase 5 lesson applied rather than restated.
/// <para>
/// Visibility is composed **into** every query that returns data rather than checked before it. A
/// check followed by a separate unscoped read is an authorization guarantee that depends on the two
/// happening close together; a single scoped query cannot come apart.
/// </para>
/// </summary>
internal sealed class CrawlReportReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : ICrawlReportReader
{
    /// <summary>A run listing is a page of history, never the whole table.</summary>
    public const int MaxRunsListed = 100;

    /// <summary>A page of a run's broken links. The bound is the reader's, not the caller's.</summary>
    public const int MaxBrokenLinksListed = 500;

    /// <summary>
    /// Rows shown per comparison bucket. The bucket's count is exact and computed in the database;
    /// only what is rendered is capped, so a large crawl cannot turn one page request into an
    /// unbounded response.
    /// </summary>
    public const int ComparisonSampleSize = 25;

    public async Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await Summaries(VisibleRuns(access).Where(run => run.EndpointId == endpointId))
            .Take(Math.Clamp(limit, 1, MaxRunsListed))
            .ToArrayAsync(cancellationToken);

    public async Task<CrawlRunSummary?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await Summaries(VisibleRuns(access).Where(run => run.Id == runId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        int limit,
        RegistryAccessContext access,
        int offset = 0,
        CancellationToken cancellationToken = default) =>
        await Project(Ordered(BrokenLinksOf(VisibleLinks(access), runId))
                .Skip(Math.Max(0, offset))
                .Take(Math.Clamp(limit, 1, MaxBrokenLinksListed)))
            .ToArrayAsync(cancellationToken);

    public async Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        // Only **full-scope** runs take part. A run that stopped on any budget — cancelled, page
        // limit, duration limit — covered part of the site, so every link it never reached would
        // surface as resolved. A partial crawl manufacturing good news is the one failure this
        // comparison must not have, and a page limit produces it just as readily as a cancellation.
        var runs = await VisibleRuns(access)
            .Where(run => run.EndpointId == endpointId
                && run.Status == CrawlRunStatuses.Completed
                && run.StopReason == CrawlStopReasons.FrontierExhausted)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => run.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        if (runs.Length == 0) return CrawlComparison.Empty;

        var links = VisibleLinks(access);
        var currentBroken = BrokenLinksOf(links, runs[0]);

        if (runs.Length == 1)
        {
            // A first crawl has nothing to compare against. Every broken link is reported as new,
            // and the null previous run is what lets a reader tell that from a run that introduced
            // them.
            return new(
                runs[0],
                null,
                await BucketAsync(currentBroken, cancellationToken),
                CrawlComparisonBucket.Empty,
                CrawlComparisonBucket.Empty,
                CrawlComparisonBucket.Empty);
        }

        var previousBroken = BrokenLinksOf(links, runs[1]);

        // The set difference is done by the database, not in memory. Loading both runs' links to
        // subtract them here would be unbounded, and bounding *that* would silently drop links —
        // which, on the previous run's side, reads as a link that was fixed.
        // Source-target identity is compared over the URLs rather than their hashes: the hash is
        // derived from the URL, and a translated string comparison carries Entity Framework's null
        // semantics — which matters, because a seed has no source page and would otherwise fail to
        // match itself. The predicate is written out at each use because a helper method cannot be
        // translated into SQL.
        var newlyBroken = currentBroken.Where(link => !previousBroken.Any(before =>
            before.SourceUrl == link.SourceUrl && before.TargetUrl == link.TargetUrl));
        var continuing = currentBroken.Where(link => previousBroken.Any(before =>
            before.SourceUrl == link.SourceUrl && before.TargetUrl == link.TargetUrl));
        var noLongerBroken = previousBroken.Where(link => !currentBroken.Any(now =>
            now.SourceUrl == link.SourceUrl && now.TargetUrl == link.TargetUrl));

        // A previously broken link that is no longer broken is only resolved if the current run
        // actually established that. A timeout, a block, a skip or an unreached target says nothing
        // about whether the link works — treating those as resolved would close findings on the
        // strength of evidence the crawl never gathered.
        var indeterminate = CrawlLinkClassifications.Indeterminate;
        var unproven = links.Where(link => link.RunId == runs[0]
            && indeterminate.Contains(link.Classification));

        return new(
            runs[0],
            runs[1],
            await BucketAsync(newlyBroken, cancellationToken),
            await BucketAsync(continuing, cancellationToken),
            await BucketAsync(
                noLongerBroken.Where(link => !unproven.Any(other =>
                    other.SourceUrl == link.SourceUrl && other.TargetUrl == link.TargetUrl)),
                cancellationToken),
            await BucketAsync(
                noLongerBroken.Where(link => unproven.Any(other =>
                    other.SourceUrl == link.SourceUrl && other.TargetUrl == link.TargetUrl)),
                cancellationToken));
    }

    private static async Task<CrawlComparisonBucket> BucketAsync(
        IQueryable<CrawlLinkResult> links,
        CancellationToken cancellationToken) =>
        new(
            await links.CountAsync(cancellationToken),
            await Project(Ordered(links).Take(ComparisonSampleSize))
                .ToArrayAsync(cancellationToken));

    /// <summary>
    /// Runs whose endpoint the requester may see. Deleted endpoints are excluded, matching the
    /// registry and the SEO view: an endpoint removed from the registry should not keep answering
    /// through a different surface, and one definition of "visible endpoint" across the read
    /// surfaces is what stops them drifting apart.
    /// </summary>
    private IQueryable<CrawlRun> VisibleRuns(RegistryAccessContext access)
    {
        ArgumentNullException.ThrowIfNull(access);
        var visibleEndpointIds = visibility
            .ApplyEndpointScope(
                dbContext.Endpoints.AsNoTracking().Where(endpoint => endpoint.DeletedAt == null),
                access,
                timeProvider.GetUtcNow())
            .Select(endpoint => endpoint.Id);
        return dbContext.CrawlRuns.AsNoTracking()
            .Where(run => visibleEndpointIds.Contains(run.EndpointId));
    }

    /// <summary>
    /// Link results whose run the requester may see, as one query. Every read of link rows starts
    /// here, so no entry point can return data through a scope it only checked separately.
    /// </summary>
    private IQueryable<CrawlLinkResult> VisibleLinks(RegistryAccessContext access)
    {
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        return dbContext.CrawlLinkResults.AsNoTracking()
            .Where(link => visibleRunIds.Contains(link.RunId));
    }

    private static IQueryable<CrawlLinkResult> BrokenLinksOf(IQueryable<CrawlLinkResult> links, Guid runId) =>
        links.Where(link => link.RunId == runId
            && link.Classification == CrawlLinkClassifications.Broken);

    /// <summary>
    /// Ordering belongs on the row, not on the projection: ordering by a member of a constructed
    /// <see cref="CrawlBrokenLink"/> is not translatable, so the sample would have to be paged in
    /// memory. Applied before <see cref="Project"/> it stays a database ORDER BY, which is what
    /// makes a bucket sample and a link page stable rather than planner-dependent.
    /// </summary>
    private static IOrderedQueryable<CrawlLinkResult> Ordered(IQueryable<CrawlLinkResult> links) =>
        links.OrderBy(link => link.TargetUrl).ThenBy(link => link.SourceUrl);

    private static IQueryable<CrawlBrokenLink> Project(IQueryable<CrawlLinkResult> links) =>
        links.Select(link => new CrawlBrokenLink(
            link.SourceUrl, link.TargetUrl, link.Classification, link.StatusCode, link.IsInternal));

    private static IQueryable<CrawlRunSummary> Summaries(IQueryable<CrawlRun> runs) =>
        runs.OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
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
                run.FinishedAt));
}

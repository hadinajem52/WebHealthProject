using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlReportReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : ICrawlReportReader
{
    public const int MaxRunsListed = 100;

    public const int MaxBrokenLinksListed = 500;

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

    public async Task<IReadOnlyList<CrawlSkipSummary>> ListSkipReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await VisibleLinks(access)
            .Where(link => link.RunId == runId && link.SkipReason != null)
            .GroupBy(link => link.SkipReason!)
            .Select(group => new { SkipReason = group.Key, Count = group.Count() })
            .OrderByDescending(summary => summary.Count)
            .ThenBy(summary => summary.SkipReason)
            .Select(summary => new CrawlSkipSummary(summary.SkipReason, summary.Count))
            .ToArrayAsync(cancellationToken);

    public async Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var runs = await VisibleRuns(access)
            .Where(run => run.EndpointId == endpointId
                && run.Status == CrawlRunStatuses.Completed
                && run.StopReason == CrawlStopReasons.FrontierExhausted
                && run.PagesFetched > 0
                && !run.CoverageLimited)
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
            return new(
                runs[0],
                null,
                await BucketAsync(currentBroken, cancellationToken),
                CrawlComparisonBucket.Empty,
                CrawlComparisonBucket.Empty,
                CrawlComparisonBucket.Empty);
        }

        var previousBroken = BrokenLinksOf(links, runs[1]);

        var newlyBroken = currentBroken.Where(link => !previousBroken.Any(before =>
            ((before.SourceUrlHash == null && link.SourceUrlHash == null)
                || (before.SourceUrlHash != null && link.SourceUrlHash != null
                    && before.SourceUrlHash == link.SourceUrlHash))
            && before.TargetUrlHash == link.TargetUrlHash));
        var continuing = currentBroken.Where(link => previousBroken.Any(before =>
            ((before.SourceUrlHash == null && link.SourceUrlHash == null)
                || (before.SourceUrlHash != null && link.SourceUrlHash != null
                    && before.SourceUrlHash == link.SourceUrlHash))
            && before.TargetUrlHash == link.TargetUrlHash));
        var noLongerBroken = previousBroken.Where(link => !currentBroken.Any(now =>
            ((now.SourceUrlHash == null && link.SourceUrlHash == null)
                || (now.SourceUrlHash != null && link.SourceUrlHash != null
                    && now.SourceUrlHash == link.SourceUrlHash))
            && now.TargetUrlHash == link.TargetUrlHash));

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
                    ((other.SourceUrlHash == null && link.SourceUrlHash == null)
                        || (other.SourceUrlHash != null && link.SourceUrlHash != null
                            && other.SourceUrlHash == link.SourceUrlHash))
                    && other.TargetUrlHash == link.TargetUrlHash)),
                cancellationToken),
            await BucketAsync(
                noLongerBroken.Where(link => unproven.Any(other =>
                    ((other.SourceUrlHash == null && link.SourceUrlHash == null)
                        || (other.SourceUrlHash != null && link.SourceUrlHash != null
                            && other.SourceUrlHash == link.SourceUrlHash))
                    && other.TargetUrlHash == link.TargetUrlHash)),
                cancellationToken));
    }

    private static async Task<CrawlComparisonBucket> BucketAsync(
        IQueryable<CrawlLinkResult> links,
        CancellationToken cancellationToken) =>
        new(
            await links.CountAsync(cancellationToken),
            await Project(Ordered(links).Take(ComparisonSampleSize))
                .ToArrayAsync(cancellationToken));

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

    private IQueryable<CrawlLinkResult> VisibleLinks(RegistryAccessContext access)
    {
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        return dbContext.CrawlLinkResults.AsNoTracking()
            .Where(link => visibleRunIds.Contains(link.RunId));
    }

    private static IQueryable<CrawlLinkResult> BrokenLinksOf(IQueryable<CrawlLinkResult> links, Guid runId) =>
        links.Where(link => link.RunId == runId
            && link.Classification == CrawlLinkClassifications.Broken);

    private static IOrderedQueryable<CrawlLinkResult> Ordered(IQueryable<CrawlLinkResult> links) =>
        links.OrderBy(link => link.TargetUrl)
            .ThenBy(link => link.SourceUrl)
            .ThenBy(link => link.TargetUrlHash)
            .ThenBy(link => link.SourceUrlHash)
            .ThenBy(link => link.Id);

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
                run.RobotsOverrideRefusedBecause,
                run.StartedAt,
                run.FinishedAt,
                run.FailureReason,
                run.CoverageLimited));
}

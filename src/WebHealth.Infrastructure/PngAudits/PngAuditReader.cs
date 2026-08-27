using Microsoft.EntityFrameworkCore;
using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngAuditReader(
    ApplicationDbContext database,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : IPngAuditReader
{
    private const int MaxRunsListed = 50;
    private const int MaxPageSize = 250;

    public async Task<PngAuditRunView?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await ProjectRuns(VisibleRuns(access).Where(run => run.Id == runId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PngAuditRunView>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await ProjectRuns(VisibleRuns(access)
                .Where(run => run.EndpointId == endpointId)
                .OrderByDescending(run => run.QueuedAt)
                .ThenByDescending(run => run.Id)
                .Take(Math.Clamp(limit, 1, MaxRunsListed)))
            .ToArrayAsync(cancellationToken);

    public async Task<PngAuditPage<PngAuditImageResultView>> ListImagesAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var page = Page(offset, limit);
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        var items = await database.PngAuditImageResults.AsNoTracking()
            .Where(result => result.RunId == runId && visibleRunIds.Contains(result.RunId))
            .OrderByDescending(result => result.Recommendation)
            .ThenBy(result => result.Classification)
            .ThenBy(result => result.ImageDisplayUrl)
            .ThenBy(result => result.Id)
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .Select(result => new PngAuditImageResultView(
                result.Id,
                result.ImageDisplayUrl,
                result.FinalDisplayUrl,
                result.Classification,
                result.ReasonCode,
                result.HttpStatusCode,
                result.ResponseBytes,
                result.Width,
                result.Height,
                result.FrameCount,
                result.UsesTransparency,
                result.Recommendation,
                result.CandidateWebpBytes,
                result.OriginalSavingsBytes,
                result.OriginalSavingsPercent,
                result.NormalizedSavingsBytes,
                result.NormalizedSavingsPercent,
                result.RecordedAt))
            .ToArrayAsync(cancellationToken);
        return ResultPage(items, page);
    }

    public async Task<PngAuditPage<PngAuditImageSourceView>> ListSourcesAsync(
        Guid imageResultId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var page = Page(offset, limit);
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        var items = await database.PngAuditImageSources.AsNoTracking()
            .Where(source => source.ImageResultId == imageResultId
                && visibleRunIds.Contains(source.ImageResult.RunId))
            .OrderBy(source => source.SourcePageDisplayUrl)
            .ThenBy(source => source.AttributeKind)
            .ThenBy(source => source.Descriptor)
            .ThenBy(source => source.Id)
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .Select(source => new PngAuditImageSourceView(
                source.Id,
                source.SourcePageDisplayUrl,
                source.AttributeKind,
                source.Descriptor))
            .ToArrayAsync(cancellationToken);
        return ResultPage(items, page);
    }

    public async Task<PngAuditPage<PngAuditDiscoverySkipView>> ListDiscoverySkipsAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var page = Page(offset, limit);
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        var items = await database.PngAuditDiscoverySkips.AsNoTracking()
            .Where(skip => skip.RunId == runId && visibleRunIds.Contains(skip.RunId))
            .OrderBy(skip => skip.SourcePageDisplayUrl)
            .ThenBy(skip => skip.AttributeKind)
            .ThenBy(skip => skip.Descriptor)
            .ThenBy(skip => skip.Id)
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .Select(skip => new PngAuditDiscoverySkipView(
                skip.Id,
                skip.SourcePageDisplayUrl,
                skip.AttributeKind,
                skip.Descriptor,
                skip.BoundedSafeRawValue,
                skip.ReasonCode,
                skip.RecordedAt))
            .ToArrayAsync(cancellationToken);
        return ResultPage(items, page);
    }

    public async Task<IReadOnlyList<PngAuditCoverageReasonView>> ListCoverageReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        return await database.PngAuditCoverageReasons.AsNoTracking()
            .Where(reason => reason.RunId == runId && visibleRunIds.Contains(reason.RunId))
            .OrderBy(reason => reason.Area)
            .ThenBy(reason => reason.ReasonCode)
            .Select(reason => new PngAuditCoverageReasonView(
                reason.Area,
                reason.ReasonCode,
                reason.Count))
            .ToArrayAsync(cancellationToken);
    }

    private IQueryable<PngAuditRun> VisibleRuns(RegistryAccessContext access)
    {
        ArgumentNullException.ThrowIfNull(access);
        var endpointIds = visibility.ApplyEndpointScope(
                database.Endpoints.AsNoTracking().Where(endpoint => endpoint.DeletedAt == null),
                access,
                timeProvider.GetUtcNow())
            .Select(endpoint => endpoint.Id);
        return database.PngAuditRuns.AsNoTracking()
            .Where(run => endpointIds.Contains(run.EndpointId));
    }

    private static IQueryable<PngAuditRunView> ProjectRuns(IQueryable<PngAuditRun> runs) =>
        runs.Select(run => new PngAuditRunView(
            run.Id,
            run.EndpointId,
            run.Source,
            run.Status,
            run.SeedUrlSnapshot,
            run.AttemptCount,
            run.PagesDiscovered,
            run.ImagesDiscovered,
            run.ImagesAnalyzed,
            run.RecommendationCount,
            run.DiscoverySkipCount,
            run.CrawlCoverageLimited,
            run.ImageAnalysisCoverageLimited,
            run.SourceMappingCoverageLimited,
            run.FailureCode,
            run.SafeDiagnostic,
            run.QueuedAt,
            run.StartedAt,
            run.FinishedAt));

    private static (int Offset, int Limit) Page(int offset, int limit) =>
        (Math.Max(0, offset), Math.Clamp(limit, 1, MaxPageSize));

    private static PngAuditPage<T> ResultPage<T>(
        IReadOnlyList<T> items,
        (int Offset, int Limit) page) =>
        new(items.Take(page.Limit).ToArray(), page.Offset, page.Limit, items.Count > page.Limit);
}

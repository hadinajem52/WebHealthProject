using Microsoft.EntityFrameworkCore;
using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PngAudits;
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

    public async Task<PngAuditLiveStatus?> GetLiveStatusAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await VisibleRuns(access)
            .Where(run => run.Id == runId)
            .Select(run => new PngAuditLiveStatus(
                run.Status,
                run.PagesDiscovered,
                run.ImagesDiscovered,
                run.ImagesAnalyzed,
                run.RecommendationCount))
            .SingleOrDefaultAsync(cancellationToken);

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
        bool archivedOnly = false,
        CancellationToken cancellationToken = default) =>
        await ProjectRuns(VisibleRuns(access)
                .Where(run => run.EndpointId == endpointId
                    && (archivedOnly ? run.ArchivedAt != null : run.ArchivedAt == null))
                .OrderByDescending(run => run.QueuedAt)
                .ThenByDescending(run => run.Id)
                .Take(Math.Clamp(limit, 1, MaxRunsListed)))
            .ToArrayAsync(cancellationToken);

    public async Task<PngAuditPage<PngAuditImageResultView>> ListImagesAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await ListImagesByFilterAsync(
            runId,
            PngAuditImageFilters.All,
            offset,
            limit,
            access,
            cancellationToken);

    public async Task<PngAuditPage<PngAuditImageResultView>> ListImagesByFilterAsync(
        Guid runId,
        string filter,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var page = Page(offset, limit);
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);
        var results = database.PngAuditImageResults.AsNoTracking()
            .Where(result => result.RunId == runId && visibleRunIds.Contains(result.RunId));
        results = FilterImages(results, PngAuditImageFilters.Normalize(filter));
        var items = await results
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
                result.SemiTransparentPixelCount,
                result.FullyTransparentPixelCount,
                result.BackgroundTransparentPixelCount,
                result.InteriorTransparentPixelCount,
                result.PixelCount,
                result.MinAlpha,
                result.Recommendation,
                result.CandidateWebpBytes,
                result.OptimizedPngBytes,
                result.OriginalSavingsBytes,
                result.OriginalSavingsPercent,
                result.ReferenceSavingsBytes,
                result.ReferenceSavingsPercent,
                result.RecordedAt,
                result.Sources.Count,
                result.Sources
                    .OrderBy(source => source.SourcePageDisplayUrl)
                    .ThenBy(source => source.AttributeKind)
                    .ThenBy(source => source.Descriptor)
                    .ThenBy(source => source.Id)
                    .Select(source => source.SourcePageDisplayUrl)
                    .FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
        return ResultPage(items, page);
    }

    public async Task<PngAuditResultSummaryView?> GetResultSummaryAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var visibleRunIds = VisibleRuns(access).Select(candidate => candidate.Id);
        var run = await VisibleRuns(access)
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new
            {
                candidate.ImagesDiscovered,
                candidate.DiscoverySkipCount
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        var results = database.PngAuditImageResults.AsNoTracking()
            .Where(result => result.RunId == runId && visibleRunIds.Contains(result.RunId));
        var classifications = await results
            .GroupBy(result => result.Classification)
            .Select(group => new { Classification = group.Key, Count = group.Count() })
            .ToDictionaryAsync(
                group => group.Classification,
                group => group.Count,
                StringComparer.Ordinal,
                cancellationToken);
        var imageResultIds = results.Select(result => result.Id);
        var sourceMappings = await database.PngAuditImageSources.AsNoTracking()
            .CountAsync(source => imageResultIds.Contains(source.ImageResultId), cancellationToken);

        int Count(string classification) => classifications.GetValueOrDefault(classification);
        var pngsAnalyzed = await results.CountAsync(
            result => result.Width != null,
            cancellationToken);
        var transparent = await results.CountAsync(
            result => result.UsesTransparency == true,
            cancellationToken);
        var transparentBackground = await results.CountAsync(
            result => result.BackgroundTransparentPixelCount != null
                && result.PixelCount != null
                && result.BackgroundTransparentPixelCount > 0
                && result.BackgroundTransparentPixelCount.Value * 100m
                    / result.PixelCount!.Value
                    >= PngTransparencyPolicy.MinBackgroundCoveragePercent,
            cancellationToken);
        var candidates = await results.CountAsync(
            result => result.Recommendation == PngAuditRecommendations.LosslessWebp,
            cancellationToken);
        var optimizePng = await results.CountAsync(
            result => result.Recommendation == PngAuditRecommendations.OptimizePng,
            cancellationToken);
        var belowThreshold = Count(PngAuditImageClassifications.BelowWebpThreshold);
        var comparisonUnavailable = Count(PngAuditImageClassifications.ComparisonUnavailable);
        var pngsCompared = await results.CountAsync(
            result => result.CandidateWebpBytes != null,
            cancellationToken);
        var totalImageResults = classifications.Values.Sum();
        return new(
            sourceMappings + run.DiscoverySkipCount,
            run.ImagesDiscovered,
            pngsAnalyzed,
            transparent,
            transparentBackground,
            pngsCompared,
            candidates,
            optimizePng,
            belowThreshold,
            comparisonUnavailable,
            run.DiscoverySkipCount + totalImageResults - pngsAnalyzed);
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

    private static IQueryable<PngAuditImageResult> FilterImages(
        IQueryable<PngAuditImageResult> results,
        string filter) => filter switch
        {
            PngAuditImageFilters.WebpCandidates => results.Where(result =>
                result.Recommendation == PngAuditRecommendations.LosslessWebp),
            PngAuditImageFilters.UsesTransparency => results.Where(result =>
                result.UsesTransparency == true),
            PngAuditImageFilters.TransparentBackground => results.Where(result =>
                result.BackgroundTransparentPixelCount != null
                && result.PixelCount != null
                && result.BackgroundTransparentPixelCount > 0
                && result.BackgroundTransparentPixelCount.Value * 100m
                    / result.PixelCount!.Value
                    >= PngTransparencyPolicy.MinBackgroundCoveragePercent),
            PngAuditImageFilters.BelowWebpThreshold => results.Where(result =>
                result.Classification == PngAuditImageClassifications.BelowWebpThreshold),
            PngAuditImageFilters.ComparisonUnavailable => results.Where(result =>
                result.Classification == PngAuditImageClassifications.ComparisonUnavailable),
            PngAuditImageFilters.AnimatedPng => results.Where(result =>
                result.Classification == PngAuditImageClassifications.AnimatedPng),
            PngAuditImageFilters.NotAnalyzed => results.Where(result =>
                PngAuditImageClassifications.NotAnalyzed.Contains(result.Classification)),
            PngAuditImageFilters.NotPng => results.Where(result =>
                result.Classification == PngAuditImageClassifications.NotPng),
            _ => results
        };

    private static (int Offset, int Limit) Page(int offset, int limit) =>
        (Math.Max(0, offset), Math.Clamp(limit, 1, MaxPageSize));

    private static PngAuditPage<T> ResultPage<T>(
        IReadOnlyList<T> items,
        (int Offset, int Limit) page) =>
        new(items.Take(page.Limit).ToArray(), page.Offset, page.Limit, items.Count > page.Limit);
}

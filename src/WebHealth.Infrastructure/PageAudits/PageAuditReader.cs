using Microsoft.EntityFrameworkCore;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

internal sealed class PageAuditReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : IPageAuditReader
{
    public const int MaxRunsListed = 50;

    public const int MaxItemsListed = 250;

    public async Task<PageAuditEndpointSummary?> GetEndpointSummaryAsync(
        Guid endpointId,
        string category,
        string strategy,
        Guid? runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await VisibleEndpoints(access)
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.DisplayUrl,
                WebsiteName = candidate.Environment.Website.Name,
                EnvironmentName = candidate.Environment.Name
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return null;
        }

        var targets = await dbContext.PageAuditTargets.AsNoTracking()
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights)
            .OrderBy(candidate => candidate.Category)
            .ThenBy(candidate => candidate.Strategy)
            .ThenBy(candidate => candidate.Id)
            .ToArrayAsync(cancellationToken);
        if (targets.Length == 0)
        {
            return PageAuditEndpointSummary.NotConfigured(
                endpoint.Id, endpoint.DisplayUrl, endpoint.WebsiteName, endpoint.EnvironmentName,
                category, strategy);
        }

        var categoryTargets = targets
            .Where(candidate => candidate.Category == category)
            .ToArray();
        var configured = categoryTargets.FirstOrDefault() ?? targets[0];
        var selectedTarget = categoryTargets.SingleOrDefault(
            candidate => candidate.Strategy == strategy);

        var runs = RunsOf(access, endpointId, category, strategy);
        var selected = runId is { } requested
            ? await Project(runs.Where(run => run.Id == requested))
                .SingleOrDefaultAsync(cancellationToken)
            : await Project(Ordered(runs))
                .FirstOrDefaultAsync(cancellationToken);

        var counts = selected is null
            ? PageAuditItemCounts.Empty
            : await CountItemsAsync(selected.RunId, cancellationToken);
        var comparison = selected is null || selectedTarget is null
            ? PageAuditComparison.None
            : await CompareAsync(access, selectedTarget.Id, selected, cancellationToken);

        return new PageAuditEndpointSummary(
            endpoint.Id,
            endpoint.DisplayUrl,
            endpoint.WebsiteName,
            endpoint.EnvironmentName,
            IsConfigured: true,
            configured.IsEnabled,
            configured.SchedulingEnabled,
            category,
            strategy,
            configured.IntervalSeconds / 3600,
            configured.SchedulingEnabled && configured.IsEnabled ? selectedTarget?.NextDueAt : null,
            selected,
            counts,
            comparison);
    }


    public async Task<IReadOnlyList<PageAuditRunSummary>> ListRunsAsync(
        Guid endpointId,
        string category,
        string strategy,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await Project(Ordered(RunsOf(access, endpointId, category, strategy))
                .Take(Math.Clamp(limit, 1, MaxRunsListed)))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<PageAuditCategorySummary>?> GetLatestCategorySummariesAsync(
        Guid endpointId,
        string strategy,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var visible = await VisibleEndpoints(access)
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);
        if (!visible)
        {
            return null;
        }

        var runs = VisibleRuns(access)
            .Where(run => run.EndpointId == endpointId
                && run.Strategy == strategy
                && PageAuditCategories.All.Contains(run.Category));
        var latestRunIds = runs
                .GroupBy(run => run.Category)
                .Select(group => group
                    .OrderByDescending(run => run.FinishedAt == null)
                    .ThenByDescending(run => run.FinishedAt)
                    .ThenByDescending(run => run.QueuedAt)
                    .ThenByDescending(run => run.Id)
                    .Select(run => run.Id)
                    .First());
        var latestRuns = await Project(runs.Where(run => latestRunIds.Contains(run.Id)))
            .ToDictionaryAsync(run => run.Category, cancellationToken);

        return PageAuditCategories.All
            .Select(category => new PageAuditCategorySummary(
                category,
                latestRuns.GetValueOrDefault(category)))
            .ToArray();
    }

    public async Task<IReadOnlyList<PageAuditItemView>> ListAuditItemsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var visibleRunIds = VisibleRuns(access).Select(run => run.Id);

        return await dbContext.PageAuditItems.AsNoTracking()
            .Where(item => item.RunId == runId && visibleRunIds.Contains(item.RunId))
            .OrderByDescending(item => item.Weight)
            .ThenBy(item => item.AuditId)
            .Take(MaxItemsListed)
            .Select(item => new PageAuditItemView(
                item.AuditId,
                item.Status,
                item.Score,
                item.ScoreDisplayMode,
                item.NumericValue,
                item.NumericUnit,
                item.Weight,
                item.GroupName,
                item.Title,
                item.Description,
                item.DisplayValue,
                item.Explanation,
                item.ErrorMessage))
            .ToArrayAsync(cancellationToken);
    }

    private async Task<PageAuditComparison> CompareAsync(
        RegistryAccessContext access,
        Guid targetId,
        PageAuditRunSummary current,
        CancellationToken cancellationToken)
    {
        if (!current.HasScore || current.FinishedAt is not { } finishedAt)
        {
            return PageAuditComparison.None;
        }

        var previous = await VisibleRuns(access)
            .Where(run => run.PageAuditTargetId == targetId
                && run.RawScore != null
                && run.FinishedAt != null
                && (run.Status == PageAuditRunStatuses.Completed
                    || run.Status == PageAuditRunStatuses.CompletedWithWarnings)
                && (run.FinishedAt < finishedAt
                    || (run.FinishedAt == finishedAt
                        && run.Id.CompareTo(current.RunId) < 0))
                && run.Strategy == current.Strategy
                && run.Locale == current.Locale)
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => new { run.Id, run.RawScore, run.LighthouseVersion })
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is null)
        {
            return new PageAuditComparison(current.RunId, null, current.Score, null, null);
        }

        var comparability =
            PageAuditNormalization.MajorVersionOf(current.LighthouseVersion)
                == PageAuditNormalization.MajorVersionOf(previous.LighthouseVersion)
                ? PageAuditComparability.Comparable
                : PageAuditComparability.LighthouseVersionChanged;

        return new PageAuditComparison(
            current.RunId,
            previous.Id,
            current.Score,
            PageAuditNormalization.ToDisplayScore(previous.RawScore),
            comparability);
    }

    private async Task<PageAuditItemCounts> CountItemsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var byStatus = await dbContext.PageAuditItems.AsNoTracking()
            .Where(item => item.RunId == runId)
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Status, entry => entry.Count, cancellationToken);

        int CountOf(string status) => byStatus.TryGetValue(status, out var count) ? count : 0;

        return new PageAuditItemCounts(
            CountOf(PageAuditItemStatuses.Failed),
            CountOf(PageAuditItemStatuses.Passed),
            CountOf(PageAuditItemStatuses.Scored),
            CountOf(PageAuditItemStatuses.Manual),
            CountOf(PageAuditItemStatuses.NotApplicable),
            CountOf(PageAuditItemStatuses.Informative),
            CountOf(PageAuditItemStatuses.Error));
    }

    private IQueryable<PageAuditRun> RunsOf(
        RegistryAccessContext access,
        Guid endpointId,
        string category,
        string strategy) =>
        VisibleRuns(access)
            .Where(run => run.EndpointId == endpointId
                && run.Category == category
                && run.Strategy == strategy);

    private static IOrderedQueryable<PageAuditRun> Ordered(IQueryable<PageAuditRun> runs) =>
        runs.OrderByDescending(run => run.FinishedAt == null)
            .ThenByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.QueuedAt)
            .ThenByDescending(run => run.Id);

    private static IQueryable<PageAuditRunSummary> Project(IQueryable<PageAuditRun> runs) =>
        runs.Select(run => new PageAuditRunSummary(
            run.Id,
            run.BatchId,
            run.EndpointId,
            run.Source,
            run.Status,
            run.RequestedUrl,
            run.FinalUrl,
            run.RawScore,
            run.Category,
            run.Strategy,
            run.Locale,
            run.LighthouseVersion,
            run.WarningSummary,
            run.FailureCategory,
            run.SafeDiagnostic,
            run.AttemptCount,
            run.QueuedAt,
            run.AnalysisAt,
            run.FinishedAt));

    private IQueryable<PageAuditRun> VisibleRuns(RegistryAccessContext access)
    {
        var visibleEndpointIds = VisibleEndpoints(access).Select(endpoint => endpoint.Id);
        return dbContext.PageAuditRuns.AsNoTracking()
            .Where(run => visibleEndpointIds.Contains(run.EndpointId));
    }

    private IQueryable<Endpoint> VisibleEndpoints(RegistryAccessContext access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return visibility.ApplyEndpointScope(
            dbContext.Endpoints.AsNoTracking().Where(endpoint => endpoint.DeletedAt == null),
            access,
            timeProvider.GetUtcNow());
    }
}

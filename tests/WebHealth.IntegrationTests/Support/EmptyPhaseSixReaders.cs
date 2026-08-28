using WebHealth.Application.Crawling;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.PageAudits;

namespace WebHealth.IntegrationTests.Support;

internal sealed class EmptySeoReader : ISeoReader
{
    public Task<SeoListPage> ListAsync(
        SeoQuery query,
        RegistryAccessContext access,
        int page,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SeoListPage([], 1, SeoQuery.PageSize, 0));
}

internal sealed class EmptyCrawlReportReader : ICrawlReportReader
{
    public static Guid RunningEndpointId { get; } = Guid.Parse("2b7d4f10-0000-0000-0000-000000000030");

    public static Guid RunningRunId { get; } = Guid.Parse("2b7d4f10-0000-0000-0000-000000000031");

    public Task<CrawlLiveStatus?> GetLiveStatusAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CrawlLiveStatus?>(runId == RunningRunId
            ? new(CrawlRunStatuses.Running, 3, 12, false)
            : null);

    public Task<int> CountBrokenLinksAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(runId == RunningRunId ? 2 : 0);

    public Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        bool archivedOnly = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlRunSummary>>(
            endpointId == RunningEndpointId ? [RunningRun()] : []);

    public Task<CrawlRunSummary?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(runId == RunningRunId ? RunningRun() : null);

    private static CrawlRunSummary RunningRun() => new(
        RunningRunId,
        RunningEndpointId,
        CrawlRunStatuses.Running,
        CrawlStopReasons.FrontierExhausted,
        PagesFetched: 3,
        LinksRecorded: 12,
        BrokenLinkCount: 0,
        RobotsOverrideGranted: false,
        RobotsOverrideRefusedBecause: null,
        StartedAt: DateTimeOffset.UnixEpoch,
        FinishedAt: null);

    public Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        int limit,
        RegistryAccessContext access,
        int offset = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlBrokenLink>>([]);

    public Task<IReadOnlyList<CrawlSkipSummary>> ListSkipReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlSkipSummary>>([]);

    public Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CrawlComparison.Empty);
}

internal sealed class EmptyPageAuditReader : IPageAuditReader
{
    public Task<IReadOnlyList<PageAuditCategorySummary>?> GetLatestCategorySummariesAsync(
        Guid endpointId,
        string strategy,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageAuditCategorySummary>?>(
            [.. PageAuditCategories.All.Select(category => new PageAuditCategorySummary(
                category,
                category == PageAuditCategories.Seo
                    ? RunOf(endpointId, category, strategy, QueuedRunId)
                    : null))]);

    public static Guid QueuedRunId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000020");
    public static Guid CompletedRunId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000021");
    public Task<PageAuditEndpointSummary?> GetEndpointSummaryAsync(
        Guid endpointId,
        string category,
        string strategy,
        Guid? runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PageAuditEndpointSummary?>(new(
            endpointId,
            "https://example.com/",
            "Example",
            "Production",
            IsConfigured: true,
            IsEnabled: true,
            SchedulingEnabled: true,
            category,
            strategy,
            24,
            null,
            ResolveRun(endpointId, category, strategy, runId),
            PageAuditItemCounts.Empty,
            PageAuditComparison.None));

    private PageAuditRunSummary? ResolveRun(
        Guid endpointId,
        string category,
        string strategy,
        Guid? runId)
    {
        if (runId == Guid.Empty)
        {
            return null;
        }
        if (runId is { } requested)
        {
            return RunOf(endpointId, category, strategy, requested);
        }
        return RunOf(endpointId, category, strategy, QueuedRunId);
    }

    private static PageAuditRunSummary RunOf(
        Guid endpointId,
        string category,
        string strategy,
        Guid runId) => new(
        runId,
        runId,
        endpointId,
        PageAuditSources.Manual,
        runId == CompletedRunId ? PageAuditRunStatuses.Completed : PageAuditRunStatuses.Queued,
        "https://example.com/",
        null,
        runId == CompletedRunId ? 0.92m : null,
        category,
        strategy,
        "en-US",
        null,
        null,
        null,
        null,
        0,
        DateTimeOffset.UtcNow,
        null,
        runId == CompletedRunId ? DateTimeOffset.UtcNow : null);

    public Task<IReadOnlyList<PageAuditRunSummary>> ListRunsAsync(
        Guid endpointId,
        string category,
        string strategy,
        int limit,
        RegistryAccessContext access,
        bool archivedOnly = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageAuditRunSummary>>([]);

    public Task<IReadOnlyList<PageAuditItemView>> ListAuditItemsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageAuditItemView>>([]);
}

internal sealed class EmptyPageAuditIncidentPolicyService : IPageAuditIncidentPolicyService
{
    private readonly Dictionary<Guid, PageAuditIncidentPolicy> policies = [];

    public Task<PageAuditIncidentPolicy> GetAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        if (!policies.TryGetValue(endpointId, out var policy))
        {
            policy = DefaultPolicy(endpointId);
            policies.Add(endpointId, policy);
        }

        return Task.FromResult(policy);
    }

    public async Task<PageAuditIncidentPolicyUpdateResult> UpdateAsync(
        Guid endpointId,
        UpdatePageAuditIncidentPolicy command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(endpointId, cancellationToken);
        var errors = PageAuditIncidentEvaluator.Validate(command);
        if (errors.Count > 0)
        {
            return new(false, false, current, errors);
        }

        if (command.Version != current.Version)
        {
            return new(
                false,
                true,
                current,
                ["These settings changed while you were editing them."]);
        }

        var updated = new PageAuditIncidentPolicy(
            endpointId,
            command.IncidentsEnabled,
            command.PerformanceScoreEnabled,
            command.PerformanceMinimumScore,
            command.AccessibilityScoreEnabled,
            command.AccessibilityMinimumScore,
            command.BestPracticesScoreEnabled,
            command.BestPracticesMinimumScore,
            command.SeoScoreEnabled,
            command.SeoMinimumScore,
            command.FirstContentfulPaintEnabled,
            command.FirstContentfulPaintMaximum,
            command.LargestContentfulPaintEnabled,
            command.LargestContentfulPaintMaximum,
            command.TotalBlockingTimeEnabled,
            command.TotalBlockingTimeMaximum,
            command.CumulativeLayoutShiftEnabled,
            command.CumulativeLayoutShiftMaximum,
            command.SpeedIndexEnabled,
            command.SpeedIndexMaximum,
            current.Version + 1);
        policies[endpointId] = updated;
        return new(true, false, updated, []);
    }

    private static PageAuditIncidentPolicy DefaultPolicy(Guid endpointId) => new(
        endpointId,
        false,
        true,
        90,
        true,
        90,
        true,
        90,
        true,
        90,
        false,
        1800,
        false,
        2500,
        false,
        200,
        false,
        0.1m,
        false,
        3400,
        1);
}

internal sealed class RecordingPageAuditRunner : IPageAuditRunner
{
    public List<Guid> Requested { get; } = [];

    public Task<PageAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        Requested.Add(endpointId);
        return Task.FromResult(
            PageAuditManualResult.Opened(
                PageAuditCategories.All.Length * PageAuditStrategies.All.Length,
                0));
    }
}

internal sealed class RecordingCrawlRunner : ICrawlRunner
{
    public List<Guid> Requested { get; } = [];

    public bool CheckExternalLinks { get; private set; }

    public bool CanQueue { get; set; } = true;

    public Task<CrawlManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        bool checkExternalLinks,
        CancellationToken cancellationToken = default)
    {
        Requested.Add(endpointId);
        CheckExternalLinks = checkExternalLinks;
        return Task.FromResult(CrawlManualResult.Queued(Guid.NewGuid()));
    }
}

internal sealed class PermissiveEndpointTestGate : IEndpointTestGate
{
    public Task<bool> CanTestEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlySet<Guid>> FilterTestableEndpointsAsync(
        IReadOnlyCollection<Guid> endpointIds,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(endpointIds.ToHashSet());

    public Task<EndpointTestBlock> DescribeTestBlockAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(endpointId == EmptyTargetRegistryReader.BlockedEndpoint.Id
            ? EndpointTestBlock.WebsiteDisabled
            : EndpointTestBlock.None);
}

using WebHealth.Application.Crawling;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Domain.PageAudits;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// The SEO and broken-link views are real read surfaces, so the shell tests — which run with no
/// database — stub their readers exactly as they already stub the dashboard's. These tests are
/// about who may reach the page; what the page shows is covered by the database foundation gate.
/// <para>
/// The stubs deliberately return data for every caller. A stub that returned nothing could not tell
/// "authorization refused this" apart from "there was nothing to show", which is precisely the
/// distinction these tests exist to make.
/// </para>
/// </summary>
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
    public Task<IReadOnlyList<CrawlRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlRunSummary>>([]);

    public Task<CrawlRunSummary?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CrawlRunSummary?>(null);

    public Task<IReadOnlyList<CrawlBrokenLink>> ListBrokenLinksAsync(
        Guid runId,
        int limit,
        RegistryAccessContext access,
        int offset = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlBrokenLink>>([]);

    public Task<CrawlComparison> CompareLatestAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CrawlComparison.Empty);
}

/// <summary>
/// The PageSpeed page's reader. It answers for every caller, so a refusal in these tests is
/// authorization refusing the request rather than the page simply having nothing to render.
/// </summary>
internal sealed class EmptyPageAuditReader : IPageAuditReader
{
    /// <summary>
    /// A configured, enabled endpoint, so the page renders its whole surface — including the
    /// Run now control, whose authorization is what several of these tests are about.
    /// </summary>
    /// <remarks>
    /// A requested run id resolves to a run, as it does in production: the real reader returns no
    /// run only when the id names nothing this endpoint owns, and the controller turns that into
    /// Not Found. A stub that answered null for every id would make the redirect after Run now
    /// look like a wrong address.
    /// </remarks>
    public Task<PageAuditEndpointSummary?> GetEndpointSummaryAsync(
        Guid endpointId,
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
            PageAuditStrategies.Mobile,
            24,
            null,
            runId is { } requested && requested != Guid.Empty
                ? RunOf(endpointId, requested)
                : null,
            PageAuditItemCounts.Empty,
            PageAuditComparison.None));

    private static PageAuditRunSummary RunOf(Guid endpointId, Guid runId) => new(
        runId,
        endpointId,
        PageAuditSources.Manual,
        PageAuditRunStatuses.Queued,
        "https://example.com/",
        null,
        null,
        PageAuditStrategies.Mobile,
        "en-US",
        null,
        null,
        null,
        null,
        0,
        DateTimeOffset.UtcNow,
        null,
        null);

    public Task<IReadOnlyList<PageAuditRunSummary>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageAuditRunSummary>>([]);

    public Task<IReadOnlyList<PageAuditItemView>> ListAuditItemsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageAuditItemView>>([]);
}

/// <summary>Records what the controller asked for instead of opening a run.</summary>
internal sealed class RecordingPageAuditRunner : IPageAuditRunner
{
    public List<Guid> Requested { get; } = [];

    public Task<PageAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        Requested.Add(endpointId);
        return Task.FromResult(PageAuditManualResult.Queued(Guid.NewGuid()));
    }
}

/// <summary>
/// Authorizes every endpoint. The tests that matter here are the ones asserting a refusal, and a
/// stub that refused everything would make those pass for the wrong reason.
/// </summary>
internal sealed class PermissiveTargetAuthorizationService : ITargetAuthorizationService
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
}

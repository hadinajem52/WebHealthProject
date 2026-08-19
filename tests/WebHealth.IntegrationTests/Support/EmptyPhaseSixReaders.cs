using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;

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

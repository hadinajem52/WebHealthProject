using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PngAudits;

namespace WebHealth.IntegrationTests.Support;

internal sealed class EmptyPngAuditReader : IPngAuditReader
{
    public static Guid RunningRunId { get; } =
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000041");

    public static Guid CompletedRunId { get; } =
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000042");

    public async Task<PngAuditLiveStatus?> GetLiveStatusAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var run = await FindRunAsync(runId, access, cancellationToken);
        return run is null
            ? null
            : new(
                run.Status,
                run.PagesDiscovered,
                run.ImagesDiscovered,
                run.ImagesAnalyzed,
                run.RecommendationCount);
    }

    public Task<PngAuditRunView?> FindRunAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PngAuditRunView?>(runId switch
        {
            _ when runId == RunningRunId => RunOf(RunningRunId, PngAuditRunStatuses.Queued),
            _ when runId == CompletedRunId => RunOf(CompletedRunId, PngAuditRunStatuses.Completed),
            _ => null
        });

    public Task<IReadOnlyList<PngAuditRunView>> ListRunsAsync(
        Guid endpointId,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PngAuditRunView>>(
            endpointId == EmptyTargetRegistryReader.Endpoint.Id
                ? [RunOf(RunningRunId, PngAuditRunStatuses.Queued)]
                : []);

    public Task<PngAuditPage<PngAuditImageResultView>> ListImagesAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ListImagesByFilterAsync(
            runId,
            PngAuditImageFilters.All,
            offset,
            limit,
            access,
            cancellationToken);

    public Task<PngAuditPage<PngAuditImageResultView>> ListImagesByFilterAsync(
        Guid runId,
        string filter,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var item = ImageResult();
        IReadOnlyList<PngAuditImageResultView> items =
            PngAuditImageFilters.Normalize(filter) is PngAuditImageFilters.All
                or PngAuditImageFilters.WebpCandidates
                ? [item]
                : [];
        return Task.FromResult(new PngAuditPage<PngAuditImageResultView>(
            offset == 0 ? items : [],
            Math.Max(0, offset),
            Math.Clamp(limit, 1, 250),
            false));
    }

    public Task<PngAuditResultSummaryView?> GetResultSummaryAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PngAuditResultSummaryView?>(
            runId == RunningRunId || runId == CompletedRunId
                ? new(3, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0)
                : null);

    public Task<PngAuditPage<PngAuditImageSourceView>> ListSourcesAsync(
        Guid imageResultId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PngAuditPage<PngAuditImageSourceView>([], offset, limit, false));

    public Task<PngAuditPage<PngAuditDiscoverySkipView>> ListDiscoverySkipsAsync(
        Guid runId,
        int offset,
        int limit,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PngAuditPage<PngAuditDiscoverySkipView>([], offset, limit, false));

    public Task<IReadOnlyList<PngAuditCoverageReasonView>> ListCoverageReasonsAsync(
        Guid runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PngAuditCoverageReasonView>>([]);

    private static PngAuditRunView RunOf(Guid runId, string status) => new(
        runId,
        EmptyTargetRegistryReader.Endpoint.Id,
        PngAuditSources.Manual,
        status,
        "https://example.com/",
        1,
        2,
        1,
        1,
        1,
        0,
        false,
        false,
        false,
        null,
        null,
        DateTimeOffset.UtcNow.AddMinutes(-1),
        status == PngAuditRunStatuses.Queued ? null : DateTimeOffset.UtcNow.AddSeconds(-30),
        status == PngAuditRunStatuses.Completed ? DateTimeOffset.UtcNow : null);

    private static PngAuditImageResultView ImageResult() => new(
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000043"),
        "https://example.com/assets/logo.png?token=REDACTED",
        "https://example.com/assets/logo.png?token=REDACTED",
        PngAuditImageClassifications.VerifiedWebpCandidate,
        null,
        200,
        188416,
        1200,
        600,
        1,
        true,
        4096,
        432000,
        424000,
        8000,
        720000,
        0,
        PngAuditRecommendations.LosslessWebp,
        114688,
        73728,
        39.1304m,
        65536,
        36.3636m,
        DateTimeOffset.UtcNow,
        3,
        "https://example.com/");
}

internal sealed class RecordingPngAuditRunner : IPngAuditRunner
{
    public List<Guid> Requested { get; } = [];

    public bool CanQueue { get; set; } = true;

    public Task<PngAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        Requested.Add(endpointId);
        return Task.FromResult(PngAuditManualResult.Queued(EmptyPngAuditReader.RunningRunId));
    }
}

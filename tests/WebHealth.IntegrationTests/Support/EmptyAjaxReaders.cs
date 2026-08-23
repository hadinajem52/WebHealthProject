using WebHealth.Application.Auditing;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Incidents;

namespace WebHealth.IntegrationTests.Support;

internal sealed class EmptyAuditTrailReader : IAuditTrailReader
{
    public Task<AuditSearchResult> SearchAsync(
        AuditSearchQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuditSearchResult([], 1, query.PageSize, 0));

    public Task<IReadOnlyList<AuditActor>> ListActorsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditActor>>([]);

    public Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> ListEntityTypesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class EmptyCheckHistoryReader : ICheckHistoryReader
{
    public Task<CheckHistoryPage?> ListForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CheckHistoryPage?>(new(
            endpointId,
            "https://example.com/",
            [],
            Math.Max(1, page),
            25,
            0,
            null,
            PerformanceComparability.Evaluate([], false)));

    public Task<CheckDetails?> FindCheckAsync(
        Guid logicalCheckId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CheckDetails?>(null);

    public Task<CheckHistoryItem?> FindLatestForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CheckHistoryItem?>(null);
}

internal sealed class EmptyRegistryMutationServices :
    IClientRegistryService,
    IWebsiteRegistryService,
    IEnvironmentRegistryService,
    IEndpointRegistryService
{
    private static readonly RegistryMutationResult Missing =
        RegistryMutationResult.Failure(RegistryMutationStatus.NotFound, "Record not found.");

    public Task<RegistryMutationResult> CreateAsync(CreateClient command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> UpdateAsync(UpdateClient command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> CreateAsync(CreateWebsite command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> UpdateAsync(UpdateWebsite command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> CreateAsync(CreateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> UpdateAsync(UpdateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> CreateAsync(CreateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> UpdateAsync(UpdateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> PurgeAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> PauseScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<RegistryMutationResult> ResumeScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();

    private static Task<RegistryMutationResult> Result() => Task.FromResult(Missing);
}

internal sealed class EmptyIncidentLifecycleService : IIncidentLifecycleService
{
    private static readonly IncidentMutationResult Missing =
        IncidentMutationResult.Failure(IncidentMutationStatus.NotFound, "Incident not found.");

    public Task<IncidentMutationResult> AcknowledgeAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> StartProgressAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> ResolveAsync(ResolveIncident command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> CloseAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> ForceCloseAsync(IncidentReasonCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> ReopenAsync(IncidentReasonCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> ReassignAsync(ReassignIncident command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();
    public Task<IncidentMutationResult> AddNoteAsync(IncidentNoteCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result();

    private static Task<IncidentMutationResult> Result() => Task.FromResult(Missing);
}

internal sealed class EmptyManualCheckService : IManualCheckService
{
    public Task<ManualCheckResult> RunNowAsync(Guid endpointId, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(ManualCheckResult.MonitorNotAvailable());

    public Task<ManualCheckResult> RunCertificateNowAsync(Guid endpointId, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(ManualCheckResult.MonitorNotAvailable());
}

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
    public Task<RegistryMutationResult> CreateAsync(CreateClient command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateClient command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.ClientId);
    public Task<RegistryMutationResult> CreateAsync(CreateWebsite command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateWebsite command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.WebsiteId);
    public Task<RegistryMutationResult> CreateAsync(CreateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EnvironmentId);
    public Task<RegistryMutationResult> CreateAsync(CreateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EndpointId);
    public Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> PurgeAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> PauseScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> ResumeScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);

    private static Task<RegistryMutationResult> Result(Guid id) =>
        Task.FromResult(RegistryMutationResult.Success(id));
}

internal sealed class EmptyIncidentLifecycleService : IIncidentLifecycleService
{
    public Task<IncidentMutationResult> AcknowledgeAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> StartProgressAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> ResolveAsync(ResolveIncident command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> CloseAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> ForceCloseAsync(IncidentReasonCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> ReopenAsync(IncidentReasonCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> ReassignAsync(ReassignIncident command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);
    public Task<IncidentMutationResult> AddNoteAsync(IncidentNoteCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);

    private static Task<IncidentMutationResult> Result(Guid id) =>
        Task.FromResult(IncidentMutationResult.Success(id));
}

internal sealed class EmptyManualCheckService : IManualCheckService
{
    public static Guid LogicalCheckId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000010");

    public Task<ManualCheckResult> RunNowAsync(Guid endpointId, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(ManualCheckResult.Queued(LogicalCheckId));

    public Task<ManualCheckResult> RunCertificateNowAsync(Guid endpointId, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(ManualCheckResult.Queued(LogicalCheckId));
}

using WebHealth.Application.Auditing;
using WebHealth.Application.Administration;
using WebHealth.Application.Assignments;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Incidents;
using WebHealth.Application.Maintenance;
using WebHealth.Domain.Monitoring;

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
        CancellationToken cancellationToken = default)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return Task.FromResult<CheckDetails?>(logicalCheckId == EmptyManualCheckService.LogicalCheckId
            ? new CheckDetails(
                LogicalCheckId: logicalCheckId,
                EndpointId: EmptyTargetRegistryReader.Endpoint.Id,
                EndpointDisplayUrl: EmptyTargetRegistryReader.Endpoint.DisplayUrl,
                Source: LogicalCheckSources.Manual,
                State: LogicalCheckStates.Completed,
                ScheduledFor: null,
                RequestedAt: completedAt,
                InitiatedByDisplayName: "Test User",
                CreatedAt: completedAt,
                StartedAt: completedAt,
                CompletedAt: completedAt,
                Outcome: "Success",
                FailureCategory: null,
                HttpStatus: 200,
                TotalDurationMs: 100,
                DnsDurationMs: null,
                ConnectDurationMs: null,
                TlsDurationMs: null,
                TtfbDurationMs: null,
                TransferredLength: null,
                DecodedLength: null,
                LengthSource: null,
                MonitorSource: null,
                MeasuredAt: completedAt,
                ResponseTruncated: false,
                SafeDiagnostic: null,
                CountsForUptime: false,
                Findings: [],
                RedirectHops: [])
            : null);
    }

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
    public Task<RegistryMutationResult> UpdateAsync(UpdateClient command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Updated(command.ClientId, command.Version);
    public Task<RegistryMutationResult> CreateAsync(CreateWebsite command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateWebsite command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Updated(command.WebsiteId, command.Version);
    public Task<RegistryMutationResult> CreateAsync(CreateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateEnvironment command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Updated(command.EnvironmentId, command.Version);
    public Task<RegistryMutationResult> CreateAsync(CreateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(Guid.NewGuid());
    public Task<RegistryMutationResult> UpdateAsync(UpdateEndpoint command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Updated(command.EndpointId, command.Version);
    public Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> PurgeAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> PauseScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);
    public Task<RegistryMutationResult> ResumeScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.EntityId);

    private static Task<RegistryMutationResult> Result(Guid id) =>
        Task.FromResult(RegistryMutationResult.Success(id));

    private static Task<RegistryMutationResult> Updated(Guid id, long version) =>
        version == -1
            ? Task.FromResult(RegistryMutationResult.Failure(
                RegistryMutationStatus.ConcurrencyConflict,
                "This record changed after you opened it. Your submitted values have been preserved."))
            : Result(id);
}

internal sealed class EmptyMaintenanceReader : IMaintenanceReader
{
    public static Guid ScopeId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000020");

    public Task<IReadOnlyList<MaintenanceWindowListItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaintenanceWindowListItem>>([]);

    public Task<MaintenanceWindowDetails?> FindAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default) =>
        Task.FromResult<MaintenanceWindowDetails?>(null);

    public Task<IReadOnlyList<MaintenanceScopeOption>> ListScopeOptionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaintenanceScopeOption>>([
            new(MaintenanceScopeKind.Endpoint, ScopeId, "Example endpoint")
        ]);
}

internal sealed class EmptyMaintenanceWindowService : IMaintenanceWindowService
{
    public static Guid WindowId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000021");

    public Task<MaintenanceMutationResult> CreateAsync(CreateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(MaintenanceMutationResult.Success(WindowId));

    public Task<MaintenanceMutationResult> UpdateAsync(UpdateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(MaintenanceMutationResult.Success(WindowId));

    public Task<MaintenanceMutationResult> CancelAsync(CancelMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        Task.FromResult(MaintenanceMutationResult.Success(command.MaintenanceWindowId));
}

internal sealed class EmptyUserAdministrationService : IUserAdministrationService
{
    public static Guid UserId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000030");
    private static ManagedUser User { get; } = new(
        UserId,
        "Example user",
        "example@example.test",
        false,
        ["Administrator"]);

    public Task<IReadOnlyList<ManagedUser>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ManagedUser>>([User]);

    public Task<ManagedUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ManagedUser?>(userId == UserId ? User : null);

    public Task<UserAdministrationResult> CreateUserAsync(CreateManagedUser command, Guid actorUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserAdministrationResult.Success(UserId));

    public Task<UserAdministrationResult> UpdateUserAsync(UpdateManagedUser command, Guid actorUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserAdministrationResult.Success(command.UserId));
}

internal sealed class EmptyTeamAdministrationService : ITeamAdministrationService
{
    public static Guid TeamId { get; } = Guid.Parse("6f1c9a20-0000-0000-0000-000000000031");

    public Task<IReadOnlyList<ManagedTeam>> ListTeamsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ManagedTeam>>([]);

    public Task<ManagedTeam?> FindTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ManagedTeam?>(null);

    public Task<TeamAdministrationResult> CreateTeamAsync(CreateManagedTeam command, Guid actorUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(TeamAdministrationResult.Success(TeamId));

    public Task<TeamAdministrationResult> UpdateTeamAsync(UpdateManagedTeam command, Guid actorUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(TeamAdministrationResult.Success(command.TeamId));
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
    public Task<IncidentArchiveResult> ArchiveResolvedAsync(RegistryAccessContext access, CancellationToken cancellationToken = default) => Task.FromResult(IncidentArchiveResult.Success(1));
    public Task<IncidentMutationResult> RestoreAsync(IncidentVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) => Result(command.IncidentId);

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

using WebHealth.Application.Registry;

namespace WebHealth.Application.Incidents;

public interface IIncidentLifecycleService
{
    Task<IncidentMutationResult> AcknowledgeAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> StartProgressAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> ResolveAsync(
        ResolveIncident command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> CloseAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> ForceCloseAsync(
        IncidentReasonCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> ReopenAsync(
        IncidentReasonCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> ReassignAsync(
        ReassignIncident command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentArchiveResult> ArchiveResolvedAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> RestoreAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IncidentMutationResult> AddNoteAsync(
        IncidentNoteCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public sealed record IncidentVersionCommand(Guid IncidentId, long Version);
public sealed record ResolveIncident(Guid IncidentId, long Version, string Category, string Note);
public sealed record IncidentReasonCommand(Guid IncidentId, long Version, string Reason);
public sealed record ReassignIncident(Guid IncidentId, long Version, Guid OwnerSubjectId);
public sealed record IncidentNoteCommand(Guid IncidentId, long Version, string Note);

public enum IncidentMutationStatus
{
    Succeeded,
    Forbidden,
    NotFound,
    ValidationFailed,
    ConcurrencyConflict
}

public sealed record IncidentMutationResult(
    IncidentMutationStatus Status,
    Guid? IncidentId,
    IReadOnlyList<ValidationError> Errors)
{
    public bool Succeeded => Status == IncidentMutationStatus.Succeeded;

    public static IncidentMutationResult Success(Guid incidentId) =>
        new(IncidentMutationStatus.Succeeded, incidentId, []);

    public static IncidentMutationResult Failure(
        IncidentMutationStatus status,
        params IEnumerable<ValidationError> errors) => new(status, null, errors.ToArray());
}

public sealed record IncidentArchiveResult(
    IncidentMutationStatus Status,
    int ArchivedCount,
    IReadOnlyList<ValidationError> Errors)
{
    public bool Succeeded => Status == IncidentMutationStatus.Succeeded;

    public static IncidentArchiveResult Success(int archivedCount) =>
        new(IncidentMutationStatus.Succeeded, archivedCount, []);

    public static IncidentArchiveResult Failure(
        IncidentMutationStatus status,
        params IEnumerable<ValidationError> errors) => new(status, 0, errors.ToArray());
}

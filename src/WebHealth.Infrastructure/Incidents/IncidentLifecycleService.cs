using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Assignments;
using WebHealth.Application.Auditing;
using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;
using WebHealth.Domain.Incidents;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Incidents;

internal sealed class IncidentLifecycleService(
    ApplicationDbContext dbContext,
    IAssignmentAccessEvaluator assignmentAccess,
    IAuditTrailWriter auditTrail,
    TimeProvider timeProvider) : IIncidentLifecycleService
{
    public Task<IncidentMutationResult> AcknowledgeAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(
            command.IncidentId,
            command.Version,
            new(IncidentStatuses.Open, IncidentLifecycleAction.Acknowledge),
            access,
            cancellationToken);

    public Task<IncidentMutationResult> StartProgressAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(
            command.IncidentId,
            command.Version,
            new(IncidentStatuses.Acknowledged, IncidentLifecycleAction.StartProgress),
            access,
            cancellationToken);

    public Task<IncidentMutationResult> ResolveAsync(
        ResolveIncident command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(
            command.IncidentId,
            command.Version,
            new(null, IncidentLifecycleAction.ResolveManually, command.Category, command.Note),
            access,
            cancellationToken);

    public Task<IncidentMutationResult> CloseAsync(
        IncidentVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(
            command.IncidentId,
            command.Version,
            new(IncidentStatuses.Resolved, IncidentLifecycleAction.Close),
            access,
            cancellationToken);

    public Task<IncidentMutationResult> ForceCloseAsync(
        IncidentReasonCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(
            command.IncidentId,
            command.Version,
            new(null, IncidentLifecycleAction.ForceClose, null, command.Reason, true),
            access,
            cancellationToken);

    public Task<IncidentMutationResult> ReopenAsync(
        IncidentReasonCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(
            command.IncidentId,
            command.Version,
            new(IncidentStatuses.Closed, IncidentLifecycleAction.Reopen, null, command.Reason, true),
            access,
            cancellationToken);

    public async Task<IncidentMutationResult> ReassignAsync(
        ReassignIncident command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!HasGlobalAccess(access))
        {
            return Fail(IncidentMutationStatus.Forbidden, "You cannot reassign incidents.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var incident = await LockAndLoadAsync(command.IncidentId, cancellationToken);
        var validation = await ValidateMutationAsync(incident, command.Version, access, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        if (!await IsActiveOwnerAsync(command.OwnerSubjectId, cancellationToken))
        {
            return Fail(IncidentMutationStatus.ValidationFailed, "Select an active incident owner.");
        }

        if (incident!.OwnerSubjectId == command.OwnerSubjectId)
        {
            return Fail(IncidentMutationStatus.ValidationFailed, "The incident already has that owner.");
        }

        var now = timeProvider.GetUtcNow();
        var before = Snapshot(incident);
        var previousOwner = incident.OwnerSubjectId;
        incident.OwnerSubjectId = command.OwnerSubjectId;
        incident.Version++;
        AddEvent(incident, access.UserId, now, IncidentEventTypes.Reassigned,
            fromOwner: previousOwner, toOwner: command.OwnerSubjectId);
        await WriteAuditAsync(access.UserId, now, IncidentAuditAction.Reassigned, before, incident, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IncidentMutationResult.Success(incident.Id);
    }

    public async Task<IncidentMutationResult> AddNoteAsync(
        IncidentNoteCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var note = command.Note.Trim();
        if (note.Length is 0 or > IncidentLifecycleEngine.MaximumNoteLength)
        {
            return Fail(IncidentMutationStatus.ValidationFailed,
                $"A note of up to {IncidentLifecycleEngine.MaximumNoteLength} characters is required.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var incident = await LockAndLoadAsync(command.IncidentId, cancellationToken);
        var validation = await ValidateMutationAsync(incident, command.Version, access, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        if (incident!.Status == IncidentStatuses.Closed)
        {
            return Fail(IncidentMutationStatus.ValidationFailed, "A closed incident is immutable.");
        }

        var now = timeProvider.GetUtcNow();
        var before = Snapshot(incident);
        incident.Version++;
        AddEvent(incident, access.UserId, now, IncidentEventTypes.NoteAdded, note: note);
        await WriteAuditAsync(access.UserId, now, IncidentAuditAction.NoteAdded, before, incident, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IncidentMutationResult.Success(incident.Id);
    }

    private async Task<IncidentMutationResult> ChangeStatusAsync(
        Guid incidentId,
        long version,
        StatusChange request,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        if (request.RequiresAdministrator && !access.Roles.Contains(ApplicationRoles.Administrator))
        {
            return Fail(IncidentMutationStatus.Forbidden, "Only an administrator can perform this action.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var incident = await LockAndLoadAsync(incidentId, cancellationToken);
        var validation = await ValidateMutationAsync(incident, version, access, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        if (request.RequiredStatus is not null && incident!.Status != request.RequiredStatus)
        {
            return Fail(IncidentMutationStatus.ValidationFailed, "The incident is no longer in the required state.");
        }

        var decision = IncidentLifecycleEngine.Evaluate(new(
            incident!.Status,
            request.Action,
            request.RequiresAdministrator,
            incident.AcknowledgedAt is not null,
            request.Category,
            request.NoteOrReason));
        if (!decision.Succeeded)
        {
            return IncidentMutationResult.Failure(IncidentMutationStatus.ValidationFailed, decision.Errors);
        }

        var now = timeProvider.GetUtcNow();
        var before = Snapshot(incident);
        ApplyStatus(incident, decision, request.Action, now);
        AddEvent(incident, access.UserId, now, IncidentEventTypes.StatusChanged,
            before.Status, incident.Status, note: request.NoteOrReason?.Trim());
        AddResolutionEvidence(incident, access.UserId, request.Action, now);
        await WriteAuditAsync(access.UserId, now, AuditAction(request.Action), before, incident, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IncidentMutationResult.Success(incident.Id);
    }

    private async Task<IncidentMutationResult?> ValidateMutationAsync(
        Incident? incident,
        long version,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        if (incident is null)
        {
            return Fail(IncidentMutationStatus.NotFound, "The incident was not found.");
        }

        if (incident.Version != version)
        {
            return Fail(IncidentMutationStatus.ConcurrencyConflict,
                "The incident changed. Reload it before trying again.");
        }

        return await CanManageAsync(incident, access, cancellationToken)
            ? null
            : Fail(IncidentMutationStatus.Forbidden, "You cannot manage this incident.");
    }

    private async Task<bool> CanManageAsync(
        Incident incident,
        RegistryAccessContext access,
        CancellationToken cancellationToken) =>
        HasGlobalAccess(access)
        || access.Roles.Contains(ApplicationRoles.DeveloperSupport)
        && await assignmentAccess.IsAssignedAsync(
            access.UserId,
            incident.OwnerSubjectId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    private static bool HasGlobalAccess(RegistryAccessContext access) =>
        access.Roles.Contains(ApplicationRoles.Administrator)
        || access.Roles.Contains(ApplicationRoles.Operations);

    private Task<bool> IsActiveOwnerAsync(Guid ownerSubjectId, CancellationToken cancellationToken) =>
        dbContext.OwnerSubjects.AnyAsync(owner => owner.Id == ownerSubjectId
            && (owner.UserId != null && dbContext.Users.Any(user =>
                    user.Id == owner.UserId && !user.IsDisabled)
                || owner.TeamId != null && dbContext.Teams.Any(team =>
                    team.Id == owner.TeamId && !team.IsDisabled)),
            cancellationToken);

    private async Task<Incident?> LockAndLoadAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("SELECT id FROM web_health.incident WHERE id = @id FOR UPDATE");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, incidentId);
        await command.ExecuteScalarAsync(cancellationToken);
        return await dbContext.Incidents.Include(incident => incident.Events)
            .SingleOrDefaultAsync(incident => incident.Id == incidentId, cancellationToken);
    }

    private void AddEvent(
        Incident incident,
        Guid? actorUserId,
        DateTimeOffset occurredAt,
        string eventType,
        string? fromStatus = null,
        string? toStatus = null,
        Guid? fromOwner = null,
        Guid? toOwner = null,
        string? note = null)
    {
        dbContext.IncidentEvents.Add(new IncidentEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ActorUserId = actorUserId,
            SequenceNumber = NextSequence(incident.Id),
            EventType = eventType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            FromOwnerSubjectId = fromOwner,
            ToOwnerSubjectId = toOwner,
            BoundedNote = note,
            OccurredAt = occurredAt
        });
    }

    private long NextSequence(Guid incidentId) =>
        dbContext.IncidentEvents.Local
            .Where(item => item.IncidentId == incidentId)
            .Select(item => item.SequenceNumber)
            .DefaultIfEmpty()
            .Max() + 1;

    private void AddResolutionEvidence(
        Incident incident,
        Guid actorUserId,
        IncidentLifecycleAction action,
        DateTimeOffset now)
    {
        if (action is not (IncidentLifecycleAction.ResolveManually or IncidentLifecycleAction.ForceClose))
        {
            return;
        }

        dbContext.IncidentEvidence.Add(new IncidentEvidence
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            EndpointMonitorId = incident.EndpointMonitorId,
            ActorUserId = actorUserId,
            EvidenceType = IncidentEvidenceTypes.Resolution,
            EvidenceRole = action.ToString(),
            BoundedSnapshot = "{\"source\":\"user\"}",
            CapturedAt = now
        });
    }

    private static void ApplyStatus(
        Incident incident,
        IncidentTransitionDecision decision,
        IncidentLifecycleAction action,
        DateTimeOffset now)
    {
        incident.Status = decision.NewStatus!;
        incident.Version++;
        if (action == IncidentLifecycleAction.Acknowledge)
        {
            incident.AcknowledgedAt = now;
        }
        else if (action is IncidentLifecycleAction.ResolveManually or IncidentLifecycleAction.ForceClose)
        {
            if (action == IncidentLifecycleAction.ResolveManually)
            {
                incident.AcknowledgedAt ??= now;
            }
            incident.ResolutionCategory = decision.ResolutionCategory;
            incident.ResolutionNote = decision.ResolutionNote;
            incident.ResolvedAt = now;
            incident.OutageDurationMs = IncidentLifecycleEngine.DurationMilliseconds(incident.OpenedAt, now);
            incident.ClosedAt = action == IncidentLifecycleAction.ForceClose ? now : null;
        }
        else if (action == IncidentLifecycleAction.Close)
        {
            incident.ClosedAt = now;
        }
        else if (action == IncidentLifecycleAction.Reopen)
        {
            incident.AcknowledgedAt = null;
            incident.RecoveryStartedAt = null;
            incident.ResolvedAt = null;
            incident.ClosedAt = null;
            incident.RecoveryDurationMs = null;
            incident.OutageDurationMs = null;
            incident.ResolutionCategory = null;
            incident.ResolutionNote = null;
        }
    }

    private Task WriteAuditAsync(
        Guid actorUserId,
        DateTimeOffset now,
        IncidentAuditAction action,
        IncidentAuditSnapshot before,
        Incident incident,
        CancellationToken cancellationToken) =>
        auditTrail.RecordIncidentMutationAsync(
            new(actorUserId, actorUserId.ToString(), now),
            action,
            before,
            Snapshot(incident),
            cancellationToken);

    internal static IncidentAuditSnapshot Snapshot(Incident incident) => new(
        incident.Id,
        incident.EndpointMonitorId,
        incident.OwnerSubjectId,
        incident.PreviousIncidentId,
        incident.IssueKey,
        incident.Severity,
        incident.Status,
        incident.RecurrenceCount,
        incident.ResolutionCategory,
        !string.IsNullOrEmpty(incident.ResolutionNote),
        incident.OpenedAt,
        incident.RecoveryStartedAt,
        incident.ResolvedAt,
        incident.ClosedAt,
        incident.RecoveryDurationMs,
        incident.OutageDurationMs,
        incident.Version);

    private static IncidentAuditAction AuditAction(IncidentLifecycleAction action) => action switch
    {
        IncidentLifecycleAction.Acknowledge => IncidentAuditAction.Acknowledged,
        IncidentLifecycleAction.StartProgress => IncidentAuditAction.InProgress,
        IncidentLifecycleAction.ResolveManually => IncidentAuditAction.Resolved,
        IncidentLifecycleAction.Close => IncidentAuditAction.Closed,
        IncidentLifecycleAction.ForceClose => IncidentAuditAction.ForceClosed,
        IncidentLifecycleAction.Reopen => IncidentAuditAction.Reopened,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private NpgsqlCommand CreateCommand(string sql) => new(
        sql,
        (NpgsqlConnection)dbContext.Database.GetDbConnection(),
        dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);

    private Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

    private static IncidentMutationResult Fail(IncidentMutationStatus status, string error) =>
        IncidentMutationResult.Failure(status, error);

    private sealed record StatusChange(
        string? RequiredStatus,
        IncidentLifecycleAction Action,
        string? Category = null,
        string? NoteOrReason = null,
        bool RequiresAdministrator = false);
}

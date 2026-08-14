using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class ClientRegistryService(
    ApplicationDbContext dbContext,
    RegistryMutationSupport support,
    IAuditTrailWriter auditTrail) : IClientRegistryService
{
    private const string ClientNameIndex = "ix_client_normalized_name_normalization_version";

    public async Task<RegistryMutationResult> CreateAsync(
        CreateClient command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var name = RegistryMutationSupport.TrimName(command.Name);
        var errors = RegistryMutationSupport.ValidateName(name);
        if (command.Notes?.Trim().Length > 2000)
        {
            errors.Add("Notes cannot exceed 2000 characters.");
        }
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await support.LockValidOwnerAsync(command.OwnerSubjectId, null, cancellationToken))
        {
            return Validation("Select an enabled user or team owner.");
        }

        var now = DateTimeOffset.UtcNow;
        var client = new Client
        {
            Id = Guid.NewGuid(),
            OwnerSubjectId = command.OwnerSubjectId,
            Name = name,
            NormalizedName = RegistryMutationSupport.NormalizeName(name),
            NormalizationVersion = NameNormalizer.Version,
            Notes = NormalizeNotes(command.Notes),
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = access.UserId,
            UpdatedAt = now,
            UpdatedByUserId = access.UserId,
            Version = 1
        };
        dbContext.Clients.Add(client);

        try
        {
            await auditTrail.RecordClientMutationAsync(
                new AuditWriteContext(access.UserId, now),
                ClientAuditAction.Created,
                null,
                ToAuditSnapshot(client, client.Notes is not null),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(client.Id);
        }
        catch (DbUpdateException exception) when (
            RegistryMutationSupport.IsConstraintViolation(exception, ClientNameIndex))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    public async Task<RegistryMutationResult> UpdateAsync(
        UpdateClient command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var name = RegistryMutationSupport.TrimName(command.Name);
        var errors = RegistryMutationSupport.ValidateName(name);
        if (command.Notes?.Trim().Length > 2000)
        {
            errors.Add("Notes cannot exceed 2000 characters.");
        }
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var client = await dbContext.Clients.SingleOrDefaultAsync(
            candidate => candidate.Id == command.ClientId,
            cancellationToken);
        if (client is null)
        {
            return NotFound();
        }

        if (client.DeletedAt is not null)
        {
            return Validation("Restore the client before editing it.");
        }

        if (!await support.LockValidOwnerAsync(
                command.OwnerSubjectId,
                client.OwnerSubjectId,
                cancellationToken))
        {
            return Validation("Select an enabled user or team owner.");
        }

        dbContext.Entry(client).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var before = ToAuditSnapshot(client, notesChanged: false);
        var now = DateTimeOffset.UtcNow;
        var notes = NormalizeNotes(command.Notes);
        var notesChanged = !string.Equals(client.Notes, notes, StringComparison.Ordinal);
        client.Name = name;
        client.NormalizedName = RegistryMutationSupport.NormalizeName(name);
        client.OwnerSubjectId = command.OwnerSubjectId;
        client.Notes = notes;
        client.IsActive = command.IsActive;
        Touch(client, access.UserId, now);

        try
        {
            await auditTrail.RecordClientMutationAsync(
                new AuditWriteContext(access.UserId, now),
                ClientAuditAction.Updated,
                before,
                ToAuditSnapshot(client, notesChanged),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(client.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            RegistryMutationSupport.IsConstraintViolation(exception, ClientNameIndex))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    public Task<RegistryMutationResult> DisableAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, ClientAuditAction.Disabled, cancellationToken);

    public Task<RegistryMutationResult> DeleteAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, ClientAuditAction.Deleted, cancellationToken);

    public Task<RegistryMutationResult> RestoreAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, ClientAuditAction.Restored, cancellationToken);

    private async Task<RegistryMutationResult> ChangeStateAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        ClientAuditAction action,
        CancellationToken cancellationToken)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var client = await dbContext.Clients.SingleOrDefaultAsync(
            candidate => candidate.Id == command.EntityId,
            cancellationToken);
        if (client is null)
        {
            return NotFound();
        }

        var stateError = ValidateState(client, action);
        if (stateError is not null)
        {
            return Validation(stateError);
        }

        dbContext.Entry(client).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var before = ToAuditSnapshot(client, notesChanged: false);
        var now = DateTimeOffset.UtcNow;
        ApplyState(client, action, access.UserId, now);

        try
        {
            await auditTrail.RecordClientMutationAsync(
                new AuditWriteContext(access.UserId, now),
                action,
                before,
                ToAuditSnapshot(client, notesChanged: false),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(client.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            RegistryMutationSupport.IsConstraintViolation(exception, ClientNameIndex))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    private static string? ValidateState(Client client, ClientAuditAction action) => action switch
    {
        ClientAuditAction.Disabled when client.DeletedAt is not null => "Restore the client before disabling it.",
        ClientAuditAction.Deleted when client.DeletedAt is not null => "The client is already deleted.",
        ClientAuditAction.Restored when client.DeletedAt is null => "The client is not deleted.",
        _ => null
    };

    private static void ApplyState(
        Client client,
        ClientAuditAction action,
        Guid actorUserId,
        DateTimeOffset now)
    {
        if (action == ClientAuditAction.Disabled)
        {
            client.IsActive = false;
        }
        else if (action == ClientAuditAction.Deleted)
        {
            client.IsActive = false;
            client.DeletedAt = now;
            client.DeletedByUserId = actorUserId;
        }
        else
        {
            client.IsActive = false;
            client.DeletedAt = null;
            client.DeletedByUserId = null;
        }

        Touch(client, actorUserId, now);
    }

    private static void Touch(Client client, Guid actorUserId, DateTimeOffset now)
    {
        client.UpdatedAt = now;
        client.UpdatedByUserId = actorUserId;
        client.Version++;
    }

    private static ClientAuditSnapshot ToAuditSnapshot(Client client, bool notesChanged) => new(
        client.Id,
        client.Name,
        client.OwnerSubjectId,
        client.IsActive,
        client.DeletedAt is not null,
        notesChanged,
        client.Version);

    private static string? NormalizeNotes(string? notes)
    {
        var normalized = RegistryMutationSupport.NormalizeOptionalText(notes);
        return normalized.Length == 0 ? null : normalized;
    }

    private async Task<RegistryMutationResult> RollBackDuplicateAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return Validation("An active client with this name already exists.");
    }

    private async Task<RegistryMutationResult> RollBackConcurrencyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return RegistryMutationResult.Failure(
            RegistryMutationStatus.ConcurrencyConflict,
            "This client changed after you opened it. Return to details, reopen the edit form, and reapply your changes.");
    }

    private static RegistryMutationResult Forbidden() =>
        RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden, "Registry management is not permitted.");

    private static RegistryMutationResult NotFound() =>
        RegistryMutationResult.Failure(RegistryMutationStatus.NotFound, "The client was not found.");

    private static RegistryMutationResult Validation(params IEnumerable<string> errors) =>
        RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, errors);
}

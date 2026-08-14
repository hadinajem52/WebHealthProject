using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class WebsiteRegistryService(
    ApplicationDbContext dbContext,
    RegistryMutationSupport support,
    IAuditTrailWriter auditTrail) : IWebsiteRegistryService
{
    private const string WebsiteNameIndex =
        "ix_website_client_id_normalized_name_normalization_version";

    public async Task<RegistryMutationResult> CreateAsync(
        CreateWebsite command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var name = RegistryMutationSupport.TrimName(command.Name);
        var errors = ValidateFields(name, command.TechnologyCms);
        if (command.IsEnabled)
        {
            errors.Add("Add an active environment before enabling the website.");
        }

        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await ClientAcceptsWebsiteAsync(command.ClientId, cancellationToken))
        {
            return Validation("Select an active client.");
        }

        if (!await support.LockValidOwnerAsync(command.OwnerSubjectId, null, cancellationToken))
        {
            return Validation("Select an enabled user or team owner.");
        }

        var now = DateTimeOffset.UtcNow;
        var website = new Website
        {
            Id = Guid.NewGuid(),
            ClientId = command.ClientId,
            OwnerSubjectId = command.OwnerSubjectId,
            Name = name,
            NormalizedName = RegistryMutationSupport.NormalizeName(name),
            NormalizationVersion = NameNormalizer.Version,
            TechnologyCms = NormalizeTechnology(command.TechnologyCms),
            IsEnabled = false,
            CreatedAt = now,
            CreatedByUserId = access.UserId,
            UpdatedAt = now,
            UpdatedByUserId = access.UserId,
            Version = 1
        };
        dbContext.Websites.Add(website);

        try
        {
            await auditTrail.RecordWebsiteMutationAsync(
                new AuditWriteContext(access.UserId, now),
                WebsiteAuditAction.Created,
                null,
                ToAuditSnapshot(website),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(website.Id);
        }
        catch (DbUpdateException exception) when (
            RegistryMutationSupport.IsConstraintViolation(exception, WebsiteNameIndex))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    public async Task<RegistryMutationResult> UpdateAsync(
        UpdateWebsite command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var name = RegistryMutationSupport.TrimName(command.Name);
        var errors = ValidateFields(name, command.TechnologyCms);
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var website = await dbContext.Websites.SingleOrDefaultAsync(
            candidate => candidate.Id == command.WebsiteId,
            cancellationToken);
        if (website is null)
        {
            return NotFound();
        }

        if (website.DeletedAt is not null)
        {
            return Validation("Restore the website before editing it.");
        }

        if (!await support.LockValidOwnerAsync(
                command.OwnerSubjectId,
                website.OwnerSubjectId,
                cancellationToken))
        {
            return Validation("Select an enabled user or team owner.");
        }

        if (command.IsEnabled
            && !await HasActiveEnvironmentAsync(website.Id, cancellationToken))
        {
            return Validation("Add an active environment before enabling the website.");
        }

        dbContext.Entry(website).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var before = ToAuditSnapshot(website);
        var now = DateTimeOffset.UtcNow;
        website.Name = name;
        website.NormalizedName = RegistryMutationSupport.NormalizeName(name);
        website.OwnerSubjectId = command.OwnerSubjectId;
        website.TechnologyCms = NormalizeTechnology(command.TechnologyCms);
        website.IsEnabled = command.IsEnabled;
        Touch(website, access.UserId, now);

        try
        {
            await auditTrail.RecordWebsiteMutationAsync(
                new AuditWriteContext(access.UserId, now),
                WebsiteAuditAction.Updated,
                before,
                ToAuditSnapshot(website),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(website.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            RegistryMutationSupport.IsConstraintViolation(exception, WebsiteNameIndex))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    public Task<RegistryMutationResult> DisableAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, WebsiteAuditAction.Disabled, cancellationToken);

    public Task<RegistryMutationResult> DeleteAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, WebsiteAuditAction.Deleted, cancellationToken);

    public Task<RegistryMutationResult> RestoreAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, WebsiteAuditAction.Restored, cancellationToken);

    private async Task<RegistryMutationResult> ChangeStateAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        WebsiteAuditAction action,
        CancellationToken cancellationToken)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var website = await dbContext.Websites.SingleOrDefaultAsync(
            candidate => candidate.Id == command.EntityId,
            cancellationToken);
        if (website is null)
        {
            return NotFound();
        }

        var stateError = ValidateState(website, action);
        if (stateError is not null)
        {
            return Validation(stateError);
        }

        dbContext.Entry(website).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var before = ToAuditSnapshot(website);
        var now = DateTimeOffset.UtcNow;
        ApplyState(website, action, access.UserId, now);

        try
        {
            await auditTrail.RecordWebsiteMutationAsync(
                new AuditWriteContext(access.UserId, now),
                action,
                before,
                ToAuditSnapshot(website),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(website.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            RegistryMutationSupport.IsConstraintViolation(exception, WebsiteNameIndex))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    private async Task<bool> ClientAcceptsWebsiteAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = await dbContext.Clients
            .FromSqlInterpolated($"""
                SELECT * FROM web_health.client
                WHERE id = {clientId}
                FOR SHARE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return client is { DeletedAt: null, IsActive: true };
    }

    private async Task<bool> HasActiveEnvironmentAsync(
        Guid websiteId,
        CancellationToken cancellationToken)
    {
        var environments = await dbContext.Environments
            .FromSqlInterpolated($"""
                SELECT * FROM web_health.environment
                WHERE website_id = {websiteId}
                  AND deleted_at IS NULL
                  AND is_active
                FOR SHARE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return environments.Count > 0;
    }

    private static List<string> ValidateFields(string name, string? technologyCms)
    {
        var errors = RegistryMutationSupport.ValidateName(name);
        if (technologyCms?.Trim().Length > 200)
        {
            errors.Add("Technology/CMS cannot exceed 200 characters.");
        }

        return errors;
    }

    private static string? ValidateState(Website website, WebsiteAuditAction action) => action switch
    {
        WebsiteAuditAction.Disabled when website.DeletedAt is not null =>
            "Restore the website before disabling it.",
        WebsiteAuditAction.Deleted when website.DeletedAt is not null => "The website is already deleted.",
        WebsiteAuditAction.Restored when website.DeletedAt is null => "The website is not deleted.",
        _ => null
    };

    private static void ApplyState(
        Website website,
        WebsiteAuditAction action,
        Guid actorUserId,
        DateTimeOffset now)
    {
        if (action == WebsiteAuditAction.Disabled)
        {
            website.IsEnabled = false;
        }
        else if (action == WebsiteAuditAction.Deleted)
        {
            website.IsEnabled = false;
            website.DeletedAt = now;
            website.DeletedByUserId = actorUserId;
        }
        else
        {
            website.IsEnabled = false;
            website.DeletedAt = null;
            website.DeletedByUserId = null;
        }

        Touch(website, actorUserId, now);
    }

    private static void Touch(Website website, Guid actorUserId, DateTimeOffset now)
    {
        website.UpdatedAt = now;
        website.UpdatedByUserId = actorUserId;
        website.Version++;
    }

    private static WebsiteAuditSnapshot ToAuditSnapshot(Website website) => new(
        website.Id,
        website.ClientId,
        website.Name,
        website.OwnerSubjectId,
        website.TechnologyCms,
        website.IsEnabled,
        website.DeletedAt is not null,
        website.Version);

    private static string? NormalizeTechnology(string? technologyCms)
    {
        var normalized = RegistryMutationSupport.NormalizeOptionalText(technologyCms);
        return normalized.Length == 0 ? null : normalized;
    }

    private async Task<RegistryMutationResult> RollBackDuplicateAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return Validation("An active website with this name already exists for the client.");
    }

    private async Task<RegistryMutationResult> RollBackConcurrencyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return RegistryMutationResult.Failure(
            RegistryMutationStatus.ConcurrencyConflict,
            "This website changed after you opened it. Return to details, reopen the edit form, and reapply your changes.");
    }

    private static RegistryMutationResult Forbidden() =>
        RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden, "Registry management is not permitted.");

    private static RegistryMutationResult NotFound() =>
        RegistryMutationResult.Failure(RegistryMutationStatus.NotFound, "The website was not found.");

    private static RegistryMutationResult Validation(params IEnumerable<string> errors) =>
        RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, errors);
}

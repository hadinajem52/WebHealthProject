using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class WebsiteRegistryService(
    ApplicationDbContext dbContext,
    RegistryMutationSupport support,
    WebsitePurgeCascade purgeCascade,
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
        var tags = TagNormalizer.Normalize(command.Tags);
        var errors = ValidateFields(name, command.TechnologyCms, tags);
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
        await ReplaceTagsAsync(website, tags, access.UserId, now, cancellationToken);

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
        var tags = TagNormalizer.Normalize(command.Tags);
        var errors = ValidateFields(name, command.TechnologyCms, tags);
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var website = await dbContext.Websites
            .Include(candidate => candidate.WebsiteTags)
            .ThenInclude(websiteTag => websiteTag.Tag)
            .SingleOrDefaultAsync(
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
        await ReplaceTagsAsync(website, tags, access.UserId, now, cancellationToken);
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

    /// <summary>
    /// The irreversible counterpart to <see cref="DeleteAsync" />, and the same two guards the
    /// endpoint purge uses: Administrator only, and the website must already be archived.
    /// </summary>
    /// <remarks>
    /// Archiving a website does not archive its endpoints, so this deletes live endpoints under
    /// an archived website. That is the point of requiring the archive step - the website has
    /// already been withdrawn from every active list, and the operator is confirming that
    /// everything beneath it goes too.
    /// </remarks>
    public async Task<RegistryMutationResult> PurgeAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The row lock stands in for the concurrency token the change tracker would normally
        // enforce: the cascade runs as set-based deletes rather than tracked saves.
        var website = await dbContext.Websites.FromSqlInterpolated($"""
            SELECT * FROM web_health.website WHERE id = {command.EntityId} FOR UPDATE
            """)
            .Include(candidate => candidate.WebsiteTags)
            .ThenInclude(websiteTag => websiteTag.Tag)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (website is null)
        {
            return NotFound();
        }

        if (website.DeletedAt is null)
        {
            return Validation("Archive the website before deleting it permanently.");
        }

        if (website.Version != command.Version)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        // Written before the cascade and deliberately outside it: audit_event references the
        // website by identifier rather than by foreign key, so it outlives the row.
        var snapshot = ToAuditSnapshot(website);
        await auditTrail.RecordWebsiteMutationAsync(
            new AuditWriteContext(access.UserId, now), WebsiteAuditAction.Purged,
            snapshot, snapshot, cancellationToken);
        await purgeCascade.ExecuteAsync(website.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RegistryMutationResult.Success(website.Id);
    }

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
        var website = await dbContext.Websites
            .Include(candidate => candidate.WebsiteTags)
            .ThenInclude(websiteTag => websiteTag.Tag)
            .SingleOrDefaultAsync(
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

    private static List<string> ValidateFields(
        string name,
        string? technologyCms,
        IReadOnlyList<NormalizedTag> tags)
    {
        var errors = RegistryMutationSupport.ValidateName(name);
        if (technologyCms?.Trim().Length > 200)
        {
            errors.Add("Technology/CMS cannot exceed 200 characters.");
        }

        if (tags.Count > TagNormalizer.MaximumTagsPerWebsite)
        {
            errors.Add($"A website can have at most {TagNormalizer.MaximumTagsPerWebsite} tags.");
        }

        if (tags.Any(tag => tag.Name.Length > TagNormalizer.MaximumTagLength))
        {
            errors.Add($"Each tag must be {TagNormalizer.MaximumTagLength} characters or fewer.");
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
        website.Version,
        website.WebsiteTags.Select(websiteTag => websiteTag.Tag.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray());

    private async Task ReplaceTagsAsync(
        Website website,
        IReadOnlyList<NormalizedTag> requestedTags,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedNames = requestedTags.Select(tag => tag.NormalizedName).ToArray();
        var tagsByName = await dbContext.Tags
            .Where(tag => normalizedNames.Contains(tag.NormalizedName)
                && tag.NormalizationVersion == NameNormalizer.Version)
            .ToDictionaryAsync(tag => tag.NormalizedName, StringComparer.Ordinal, cancellationToken);

        foreach (var requestedTag in requestedTags)
        {
            if (!tagsByName.ContainsKey(requestedTag.NormalizedName))
            {
                await InsertTagIfMissingAsync(requestedTag, actorUserId, now, cancellationToken);
            }
        }

        tagsByName = await dbContext.Tags
            .Where(tag => normalizedNames.Contains(tag.NormalizedName)
                && tag.NormalizationVersion == NameNormalizer.Version)
            .ToDictionaryAsync(tag => tag.NormalizedName, StringComparer.Ordinal, cancellationToken);

        foreach (var requestedTag in requestedTags)
        {
            var tag = tagsByName[requestedTag.NormalizedName];
            var existing = website.WebsiteTags.SingleOrDefault(websiteTag => websiteTag.TagId == tag.Id);
            if (existing is null)
            {
                website.WebsiteTags.Add(new WebsiteTag
                {
                    WebsiteId = website.Id,
                    TagId = tag.Id,
                    Tag = tag,
                    CreatedAt = now,
                    CreatedByUserId = actorUserId
                });
            }
        }

        var requestedIds = tagsByName.Values.Select(tag => tag.Id).ToHashSet();
        foreach (var websiteTag in website.WebsiteTags.Where(item => !requestedIds.Contains(item.TagId)).ToArray())
        {
            website.WebsiteTags.Remove(websiteTag);
            dbContext.WebsiteTags.Remove(websiteTag);
        }
    }

    private Task<int> InsertTagIfMissingAsync(
        NormalizedTag tag,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO web_health.tag
                (id, name, normalized_name, normalization_version, created_at, created_by_user_id, version)
            VALUES
                ({Guid.NewGuid()}, {tag.Name}, {tag.NormalizedName}, {NameNormalizer.Version}, {now}, {actorUserId}, 1)
            ON CONFLICT (normalized_name, normalization_version) DO NOTHING
            """, cancellationToken);

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

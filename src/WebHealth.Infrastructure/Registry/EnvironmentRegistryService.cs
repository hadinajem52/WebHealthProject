using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EnvironmentRegistryService(
    ApplicationDbContext dbContext,
    IAuditTrailWriter auditTrail) : IEnvironmentRegistryService
{
    private const string EnvironmentNameIndex =
        "ix_environment_website_id_normalized_name_normalization_version";

    public async Task<RegistryMutationResult> CreateAsync(
        CreateEnvironment command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var input = NormalizeInput(command.Name, command.EnvironmentType, command.BaseUrl);
        if (input.Errors.Count > 0)
        {
            return Validation(input.Errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockWebsiteAsync(command.WebsiteId, cancellationToken))
        {
            return Validation("Select an active website.");
        }

        var now = DateTimeOffset.UtcNow;
        var environment = new WebsiteEnvironment
        {
            Id = Guid.NewGuid(),
            WebsiteId = command.WebsiteId,
            Name = input.Name,
            NormalizedName = NameNormalizer.Normalize(input.Name),
            NormalizationVersion = NameNormalizer.Version,
            EnvironmentType = input.EnvironmentType,
            IsProduction = input.IsProduction,
            BaseUrl = input.BaseUrl,
            IsActive = command.IsActive,
            CreatedAt = now,
            CreatedByUserId = access.UserId,
            UpdatedAt = now,
            UpdatedByUserId = access.UserId,
            Version = 1
        };
        dbContext.Environments.Add(environment);

        try
        {
            await auditTrail.RecordEnvironmentMutationAsync(
                new(access.UserId, now),
                EnvironmentAuditAction.Created,
                null,
                ToAudit(environment, environment.BaseUrl is not null),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(environment.Id);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    public async Task<RegistryMutationResult> UpdateAsync(
        UpdateEnvironment command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var input = NormalizeInput(command.Name, command.EnvironmentType, command.BaseUrl);
        if (input.Errors.Count > 0)
        {
            return Validation(input.Errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var environment = await dbContext.Environments
            .Include(candidate => candidate.Endpoints).ThenInclude(endpoint => endpoint.Monitors)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EnvironmentId, cancellationToken);
        if (environment is null)
        {
            return NotFound();
        }

        if (environment.DeletedAt is not null)
        {
            return Validation("Restore the environment before editing it.");
        }

        if (!command.IsActive && !await CanBecomeInactiveAsync(environment, cancellationToken))
        {
            return Validation("Disable the website before removing its final active environment.");
        }

        if (input.IsProduction && await HasUnapprovedHttpEndpointAsync(environment.Id, cancellationToken))
        {
            return Validation("Every HTTP endpoint requires an administrator-approved exception before this environment can become Production.");
        }

        dbContext.Entry(environment).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var baseUrlChanged = !string.Equals(environment.BaseUrl, input.BaseUrl, StringComparison.Ordinal);
        var before = ToAudit(environment, baseUrlChanged: false);
        var now = DateTimeOffset.UtcNow;
        environment.Name = input.Name;
        environment.NormalizedName = NameNormalizer.Normalize(input.Name);
        environment.EnvironmentType = input.EnvironmentType;
        environment.IsProduction = input.IsProduction;
        environment.BaseUrl = input.BaseUrl;
        environment.IsActive = command.IsActive;
        Touch(environment, access.UserId, now);
        UpdateMonitorIntervals(environment, access.UserId, now);

        try
        {
            await auditTrail.RecordEnvironmentMutationAsync(
                new(access.UserId, now),
                EnvironmentAuditAction.Updated,
                before,
                ToAudit(environment, baseUrlChanged),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(environment.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    public Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EnvironmentAuditAction.Disabled, cancellationToken);

    public Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EnvironmentAuditAction.Deleted, cancellationToken);

    public Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EnvironmentAuditAction.Restored, cancellationToken);

    private async Task<RegistryMutationResult> ChangeStateAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        EnvironmentAuditAction action,
        CancellationToken cancellationToken)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var environment = await dbContext.Environments
            .Include(candidate => candidate.Endpoints).ThenInclude(endpoint => endpoint.Monitors)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EntityId, cancellationToken);
        if (environment is null)
        {
            return NotFound();
        }

        var stateError = ValidateState(environment, action);
        if (stateError is not null)
        {
            return Validation(stateError);
        }

        if (action is EnvironmentAuditAction.Disabled or EnvironmentAuditAction.Deleted
            && !await CanBecomeInactiveAsync(environment, cancellationToken))
        {
            return Validation("Disable the website before removing its final active environment.");
        }

        dbContext.Entry(environment).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var before = ToAudit(environment, baseUrlChanged: false);
        var now = DateTimeOffset.UtcNow;
        ApplyState(environment, action, access.UserId, now);

        try
        {
            await auditTrail.RecordEnvironmentMutationAsync(
                new(access.UserId, now), action, before, ToAudit(environment, false), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(environment.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return await RollBackDuplicateAsync(transaction, cancellationToken);
        }
    }

    private async Task<bool> LockWebsiteAsync(Guid websiteId, CancellationToken cancellationToken)
    {
        var website = await dbContext.Websites.FromSqlInterpolated($"""
            SELECT * FROM web_health.website WHERE id = {websiteId} FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return website is { DeletedAt: null };
    }

    private async Task<bool> CanBecomeInactiveAsync(WebsiteEnvironment environment, CancellationToken cancellationToken)
    {
        var websiteEnabled = await dbContext.Websites.Where(website => website.Id == environment.WebsiteId)
            .Select(website => website.IsEnabled).SingleAsync(cancellationToken);
        if (!websiteEnabled)
        {
            return true;
        }

        return await dbContext.Environments.AnyAsync(candidate =>
            candidate.WebsiteId == environment.WebsiteId
            && candidate.Id != environment.Id
            && candidate.DeletedAt == null
            && candidate.IsActive, cancellationToken);
    }

    private Task<bool> HasUnapprovedHttpEndpointAsync(Guid environmentId, CancellationToken cancellationToken) =>
        dbContext.Endpoints.AnyAsync(endpoint =>
            endpoint.EnvironmentId == environmentId
            && endpoint.DeletedAt == null
            && endpoint.NormalizedUrl.StartsWith("http://")
            && (endpoint.HttpExceptionReason == null
                || endpoint.HttpExceptionApprovedByUserId == null
                || endpoint.HttpExceptionApprovedAt == null
                || !dbContext.UserRoles.Any(userRole =>
                    userRole.UserId == endpoint.HttpExceptionApprovedByUserId
                    && dbContext.Roles.Any(role =>
                        role.Id == userRole.RoleId && role.Name == ApplicationRoles.Administrator))),
            cancellationToken);

    private static EnvironmentInput NormalizeInput(string name, string environmentType, string? baseUrl)
    {
        var displayName = NameNormalizer.TrimDisplayName(name);
        var type = environmentType.Trim();
        var errors = new List<string>();
        if (displayName.Length == 0 || displayName.Length > 100)
        {
            errors.Add("Enter an environment name of 100 characters or fewer.");
        }

        if (!EnvironmentTypes.All.Contains(type, StringComparer.Ordinal))
        {
            errors.Add("Select a supported environment type.");
        }

        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl, errors);
        var isProduction = type == EnvironmentTypes.Production;
        if (isProduction && normalizedBaseUrl?.StartsWith("http://", StringComparison.Ordinal) == true)
        {
            errors.Add("Production environment base URLs must use HTTPS.");
        }

        return new(displayName, type, isProduction, normalizedBaseUrl, errors);
    }

    private static string? NormalizeBaseUrl(string? baseUrl, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var result = EndpointUrlNormalizer.Normalize(baseUrl);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                errors.Add($"Base URL: {error}");
            }

            return null;
        }

        return result.NormalizedUrl;
    }

    private static void UpdateMonitorIntervals(WebsiteEnvironment environment, Guid actorId, DateTimeOffset now)
    {
        var interval = RegistryDefaults.GetHttpIntervalSeconds(environment.IsProduction);
        foreach (var monitor in environment.Endpoints.SelectMany(endpoint => endpoint.Monitors))
        {
            var effectiveInterval = MonitorIntervalOverride.GetSeconds(monitor.BoundedOverrides) ?? interval;
            monitor.IntervalSeconds = effectiveInterval;
            monitor.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                monitor.ScheduleAnchor, effectiveInterval, now);
            monitor.ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
                monitor.Endpoint.NormalizedUrl,
                environment.IsProduction,
                effectiveInterval,
                monitor.TimeoutSeconds,
                monitor.FailureConfirmationCount,
                monitor.RecoveryConfirmationCount,
                monitor.WarningThresholdMs,
                monitor.CriticalThresholdMs);
            monitor.UpdatedAt = now;
            monitor.UpdatedByUserId = actorId;
            monitor.Version++;
        }
    }

    private static void ApplyState(WebsiteEnvironment environment, EnvironmentAuditAction action, Guid actorId, DateTimeOffset now)
    {
        environment.IsActive = false;
        if (action == EnvironmentAuditAction.Deleted)
        {
            environment.DeletedAt = now;
            environment.DeletedByUserId = actorId;
        }
        else if (action == EnvironmentAuditAction.Restored)
        {
            environment.DeletedAt = null;
            environment.DeletedByUserId = null;
        }

        Touch(environment, actorId, now);
    }

    private static string? ValidateState(WebsiteEnvironment environment, EnvironmentAuditAction action) => action switch
    {
        EnvironmentAuditAction.Disabled when environment.DeletedAt is not null => "Restore the environment before disabling it.",
        EnvironmentAuditAction.Deleted when environment.DeletedAt is not null => "The environment is already deleted.",
        EnvironmentAuditAction.Restored when environment.DeletedAt is null => "The environment is not deleted.",
        _ => null
    };

    private static void Touch(WebsiteEnvironment environment, Guid actorId, DateTimeOffset now)
    {
        environment.UpdatedAt = now;
        environment.UpdatedByUserId = actorId;
        environment.Version++;
    }

    private static EnvironmentAuditSnapshot ToAudit(WebsiteEnvironment environment, bool baseUrlChanged) => new(
        environment.Id, environment.WebsiteId, environment.Name, environment.EnvironmentType,
        environment.IsProduction, baseUrlChanged, environment.IsActive,
        environment.DeletedAt is not null, environment.Version);

    private static bool IsDuplicate(DbUpdateException exception) =>
        RegistryMutationSupport.IsConstraintViolation(exception, EnvironmentNameIndex);

    private async Task<RegistryMutationResult> RollBackDuplicateAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return Validation("An environment with this name already exists for the website.");
    }

    private async Task<RegistryMutationResult> RollBackConcurrencyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return RegistryMutationResult.Failure(RegistryMutationStatus.ConcurrencyConflict,
            "This environment changed after you opened it. Return to details and reopen the edit form.");
    }

    private static RegistryMutationResult Forbidden() => RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden, "Registry management is not permitted.");
    private static RegistryMutationResult NotFound() => RegistryMutationResult.Failure(RegistryMutationStatus.NotFound, "The environment was not found.");
    private static RegistryMutationResult Validation(params IEnumerable<string> errors) => RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, errors);

    private sealed record EnvironmentInput(
        string Name, string EnvironmentType, bool IsProduction, string? BaseUrl, IReadOnlyList<string> Errors);
}

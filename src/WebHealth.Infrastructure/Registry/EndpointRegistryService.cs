using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EndpointRegistryService(
    ApplicationDbContext dbContext,
    RegistryMutationSupport mutationSupport,
    IAuditTrailWriter auditTrail) : IEndpointRegistryService
{
    private const string EndpointUrlIndex = "ux_endpoint_environment_url_hash_version_active";

    public async Task<RegistryMutationResult> CreateAsync(
        CreateEndpoint command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var url = EndpointUrlNormalizer.Normalize(command.Url);
        if (!url.Succeeded)
        {
            return Validation(url.Errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var environment = await LockEnvironmentAsync(command.EnvironmentId, cancellationToken);
        if (environment is null)
        {
            return Validation("Select an active environment under a non-deleted website.");
        }

        if (!await IsValidOwnerAsync(command.OwnerSubjectId, null, cancellationToken))
        {
            return Validation("Select an enabled user or team owner, or inherit the website owner.");
        }

        var exception = DecideHttpException(url.NormalizedUrl!, command.HttpExceptionReason, environment.IsProduction, access, null);
        if (exception.Error is not null)
        {
            return Validation(exception.Error);
        }

        var now = DateTimeOffset.UtcNow;
        var endpoint = CreateEndpointEntity(command, access.UserId, url, exception, now);
        dbContext.Endpoints.Add(endpoint);
        dbContext.EndpointMonitors.Add(CreateMonitor(endpoint, environment.IsProduction, access.UserId, now));

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), EndpointAuditAction.Created, null,
                ToAudit(endpoint, urlChanged: true, httpExceptionChanged: exception.Reason is not null),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateException exceptionError) when (IsDuplicate(exceptionError))
        {
            return await RollBackDuplicateAsync(
                transaction, command.EnvironmentId, url.NormalizedUrl!, url.NormalizedUrlHash!, cancellationToken);
        }
    }

    public async Task<RegistryMutationResult> UpdateAsync(
        UpdateEndpoint command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var url = EndpointUrlNormalizer.Normalize(command.Url);
        if (!url.Succeeded)
        {
            return Validation(url.Errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var endpoint = await dbContext.Endpoints.Include(candidate => candidate.Environment)
            .ThenInclude(environment => environment.Website)
            .Include(candidate => candidate.Monitors)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EndpointId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        if (endpoint.DeletedAt is not null)
        {
            return Validation("Restore the endpoint before editing it.");
        }

        if (!await IsValidOwnerAsync(command.OwnerSubjectId, endpoint.OwnerSubjectId, cancellationToken))
        {
            return Validation("Select an enabled user or team owner, or inherit the website owner.");
        }

        var exception = DecideHttpException(url.NormalizedUrl!, command.HttpExceptionReason,
            endpoint.Environment.IsProduction, access, endpoint);
        if (exception.Error is not null)
        {
            return Validation(exception.Error);
        }

        dbContext.Entry(endpoint).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var urlChanged = !string.Equals(endpoint.NormalizedUrl, url.NormalizedUrl, StringComparison.Ordinal);
        var exceptionChanged = !string.Equals(endpoint.HttpExceptionReason, exception.Reason, StringComparison.Ordinal)
            || endpoint.HttpExceptionApprovedByUserId != exception.ApprovedByUserId;
        var before = ToAudit(endpoint, urlChanged: false, httpExceptionChanged: false);
        var now = DateTimeOffset.UtcNow;
        ApplyEndpointUpdate(endpoint, command, url, exception, access.UserId, now);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), EndpointAuditAction.Updated, before,
                ToAudit(endpoint, urlChanged, exceptionChanged), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exceptionError) when (IsDuplicate(exceptionError))
        {
            return await RollBackDuplicateAsync(
                transaction, endpoint.EnvironmentId, url.NormalizedUrl!, url.NormalizedUrlHash!, cancellationToken);
        }
    }

    public Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EndpointAuditAction.Disabled, cancellationToken);

    public Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EndpointAuditAction.Deleted, cancellationToken);

    public Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EndpointAuditAction.Restored, cancellationToken);

    private async Task<RegistryMutationResult> ChangeStateAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        EndpointAuditAction action,
        CancellationToken cancellationToken)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var endpoint = await dbContext.Endpoints.Include(candidate => candidate.Monitors)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EntityId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        var stateError = ValidateState(endpoint, action);
        if (stateError is not null)
        {
            return Validation(stateError);
        }

        dbContext.Entry(endpoint).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var before = ToAudit(endpoint, false, false);
        var now = DateTimeOffset.UtcNow;
        ApplyState(endpoint, action, access.UserId, now);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), action, before, ToAudit(endpoint, false, false), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return await RollBackDuplicateAsync(
                transaction, endpoint.EnvironmentId, endpoint.NormalizedUrl, endpoint.NormalizedUrlHash, cancellationToken);
        }
    }

    private async Task<WebsiteEnvironment?> LockEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var environment = await dbContext.Environments.FromSqlInterpolated($"""
            SELECT * FROM web_health.environment WHERE id = {environmentId} FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (environment is not { DeletedAt: null, IsActive: true })
        {
            return null;
        }

        var websiteExists = await dbContext.Websites.AnyAsync(website =>
            website.Id == environment.WebsiteId && website.DeletedAt == null, cancellationToken);
        return websiteExists ? environment : null;
    }

    private Task<bool> IsValidOwnerAsync(Guid? ownerId, Guid? retainedOwnerId, CancellationToken cancellationToken) =>
        ownerId is null
            ? Task.FromResult(true)
            : mutationSupport.LockValidOwnerAsync(ownerId.Value, retainedOwnerId, cancellationToken);

    private static HttpExceptionDecision DecideHttpException(
        string normalizedUrl,
        string? submittedReason,
        bool isProduction,
        RegistryAccessContext access,
        Endpoint? existing)
    {
        if (!normalizedUrl.StartsWith("http://", StringComparison.Ordinal) || !isProduction)
        {
            return new(null, null, null, null);
        }

        var reason = submittedReason?.Trim();
        if (reason?.Length > 500)
        {
            return new(null, null, null, "The HTTP exception reason cannot exceed 500 characters.");
        }

        var canRetain = existing is not null
            && string.Equals(existing.NormalizedUrl, normalizedUrl, StringComparison.Ordinal)
            && string.Equals(existing.HttpExceptionReason, reason, StringComparison.Ordinal)
            && existing.HttpExceptionApprovedByUserId is not null
            && existing.HttpExceptionApprovedAt is not null;
        if (canRetain)
        {
            return new(reason, existing!.HttpExceptionApprovedByUserId, existing.HttpExceptionApprovedAt, null);
        }

        if (!access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
        {
            return new(null, null, null, "Only an Administrator can approve HTTP for a Production endpoint.");
        }

        return string.IsNullOrWhiteSpace(reason)
            ? new(null, null, null, "Enter a required reason for the Production HTTP exception.")
            : new(reason, access.UserId, DateTimeOffset.UtcNow, null);
    }

    private static Endpoint CreateEndpointEntity(
        CreateEndpoint command,
        Guid actorId,
        EndpointUrlNormalizationResult url,
        HttpExceptionDecision exception,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            EnvironmentId = command.EnvironmentId,
            OwnerSubjectId = command.OwnerSubjectId,
            DisplayUrl = url.DisplayUrl!,
            NormalizedUrl = url.NormalizedUrl!,
            NormalizedUrlHash = url.NormalizedUrlHash!,
            NormalizationVersion = EndpointUrlNormalizer.Version,
            IsEnabled = command.IsEnabled,
            HttpExceptionReason = exception.Reason,
            HttpExceptionApprovedByUserId = exception.ApprovedByUserId,
            HttpExceptionApprovedAt = exception.ApprovedAt,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };

    private static EndpointMonitor CreateMonitor(Endpoint endpoint, bool isProduction, Guid actorId, DateTimeOffset now)
    {
        var interval = RegistryDefaults.GetHttpIntervalSeconds(isProduction);
        return new EndpointMonitor
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            PolicyProfileId = RegistryDefaults.HttpAvailabilityPolicyProfileId,
            MonitorType = RegistryDefaults.HttpAvailabilityMonitorType,
            BoundedOverrides = "{}",
            ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
                endpoint.NormalizedUrl, interval, RegistryDefaults.HttpTimeoutSeconds),
            IntervalSeconds = interval,
            TimeoutSeconds = RegistryDefaults.HttpTimeoutSeconds,
            FailureConfirmationCount = 2,
            RecoveryConfirmationCount = 2,
            WarningThresholdMs = 1000,
            CriticalThresholdMs = 3000,
            IsEnabled = endpoint.IsEnabled,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };
    }

    private static void ApplyEndpointUpdate(
        Endpoint endpoint,
        UpdateEndpoint command,
        EndpointUrlNormalizationResult url,
        HttpExceptionDecision exception,
        Guid actorId,
        DateTimeOffset now)
    {
        endpoint.OwnerSubjectId = command.OwnerSubjectId;
        endpoint.DisplayUrl = url.DisplayUrl!;
        endpoint.NormalizedUrl = url.NormalizedUrl!;
        endpoint.NormalizedUrlHash = url.NormalizedUrlHash!;
        endpoint.NormalizationVersion = EndpointUrlNormalizer.Version;
        endpoint.IsEnabled = command.IsEnabled;
        endpoint.HttpExceptionReason = exception.Reason;
        endpoint.HttpExceptionApprovedByUserId = exception.ApprovedByUserId;
        endpoint.HttpExceptionApprovedAt = exception.ApprovedAt;
        Touch(endpoint, actorId, now);
        foreach (var monitor in endpoint.Monitors.Where(monitor => monitor.DeletedAt == null))
        {
            monitor.IsEnabled = endpoint.IsEnabled;
            monitor.ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
                endpoint.NormalizedUrl, monitor.IntervalSeconds, monitor.TimeoutSeconds);
            monitor.UpdatedAt = now;
            monitor.UpdatedByUserId = actorId;
            monitor.Version++;
        }
    }

    private static void ApplyState(Endpoint endpoint, EndpointAuditAction action, Guid actorId, DateTimeOffset now)
    {
        endpoint.IsEnabled = false;
        if (action == EndpointAuditAction.Deleted)
        {
            endpoint.DeletedAt = now;
            endpoint.DeletedByUserId = actorId;
        }
        else if (action == EndpointAuditAction.Restored)
        {
            endpoint.DeletedAt = null;
            endpoint.DeletedByUserId = null;
        }

        foreach (var monitor in endpoint.Monitors)
        {
            monitor.IsEnabled = false;
            monitor.DeletedAt = action == EndpointAuditAction.Deleted ? now : null;
            monitor.DeletedByUserId = action == EndpointAuditAction.Deleted ? actorId : null;
            monitor.UpdatedAt = now;
            monitor.UpdatedByUserId = actorId;
            monitor.Version++;
        }

        Touch(endpoint, actorId, now);
    }

    private static void Touch(Endpoint endpoint, Guid actorId, DateTimeOffset now)
    {
        endpoint.UpdatedAt = now;
        endpoint.UpdatedByUserId = actorId;
        endpoint.Version++;
    }

    private static EndpointAuditSnapshot ToAudit(Endpoint endpoint, bool urlChanged, bool httpExceptionChanged) => new(
        endpoint.Id, endpoint.EnvironmentId, endpoint.OwnerSubjectId,
        Convert.ToHexString(endpoint.NormalizedUrlHash).ToLowerInvariant(), endpoint.NormalizationVersion,
        urlChanged, endpoint.IsEnabled, endpoint.HttpExceptionReason is not null,
        httpExceptionChanged, endpoint.DeletedAt is not null, endpoint.Version);

    private static string? ValidateState(Endpoint endpoint, EndpointAuditAction action) => action switch
    {
        EndpointAuditAction.Disabled when endpoint.DeletedAt is not null => "Restore the endpoint before disabling it.",
        EndpointAuditAction.Deleted when endpoint.DeletedAt is not null => "The endpoint is already deleted.",
        EndpointAuditAction.Restored when endpoint.DeletedAt is null => "The endpoint is not deleted.",
        _ => null
    };

    private static bool IsDuplicate(DbUpdateException exception) =>
        RegistryMutationSupport.IsConstraintViolation(exception, EndpointUrlIndex);

    private async Task<RegistryMutationResult> RollBackDuplicateAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Guid environmentId,
        string normalizedUrl,
        byte[] normalizedUrlHash,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var existingUrl = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.EnvironmentId == environmentId
                && endpoint.DeletedAt == null
                && endpoint.NormalizationVersion == EndpointUrlNormalizer.Version
                && endpoint.NormalizedUrlHash.SequenceEqual(normalizedUrlHash))
            .Select(endpoint => endpoint.NormalizedUrl)
            .SingleOrDefaultAsync(cancellationToken);
        return string.Equals(existingUrl, normalizedUrl, StringComparison.Ordinal)
            ? Validation("An endpoint with this normalized URL already exists in the environment.")
            : Validation("A URL identity hash collision was detected. No endpoint was saved.");
    }

    private async Task<RegistryMutationResult> RollBackConcurrencyAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return RegistryMutationResult.Failure(RegistryMutationStatus.ConcurrencyConflict,
            "This endpoint changed after you opened it. Return to details and reopen the edit form.");
    }

    private static RegistryMutationResult Forbidden() => RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden, "Registry management is not permitted.");
    private static RegistryMutationResult NotFound() => RegistryMutationResult.Failure(RegistryMutationStatus.NotFound, "The endpoint was not found.");
    private static RegistryMutationResult Validation(params IEnumerable<string> errors) => RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, errors);

    private sealed record HttpExceptionDecision(
        string? Reason, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? Error);
}

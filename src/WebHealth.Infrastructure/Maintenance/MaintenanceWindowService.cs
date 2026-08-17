using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Maintenance;
using WebHealth.Application.Registry;
using WebHealth.Domain.Maintenance;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceWindowService(ApplicationDbContext dbContext, IAuditTrailWriter auditTrail, TimeProvider timeProvider) : IMaintenanceWindowService
{
    public async Task<MaintenanceMutationResult> CreateAsync(CreateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return Fail(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        var errors = await ValidateAsync(command, cancellationToken);
        if (errors.Count > 0) return Fail(MaintenanceMutationStatus.ValidationFailed, errors);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var window = CreateWindow(command, access.UserId, now);
        dbContext.MaintenanceWindows.Add(window);
        await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Created, null, ToAudit(window, command.Scope, true), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MaintenanceMutationResult.Success(window.Id);
    }

    public async Task<MaintenanceMutationResult> UpdateAsync(UpdateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return Fail(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        var create = new CreateMaintenanceWindow(command.Scope, command.StartsAt, command.EndsAt, command.TimezoneId, command.Reason, command.SuppressionPolicy, command.PauseEscalation, command.ContinueFailureCounter);
        var errors = await ValidateAsync(create, cancellationToken);
        if (errors.Count > 0) return Fail(MaintenanceMutationStatus.ValidationFailed, errors);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var original = await dbContext.MaintenanceWindows.Include(item => item.Targets).Include(item => item.Occurrences)
            .SingleOrDefaultAsync(item => item.Id == command.MaintenanceWindowId, cancellationToken);
        if (original is null) return Fail(MaintenanceMutationStatus.NotFound, "The maintenance window was not found.");
        if (original.DeletedAt is not null) return Fail(MaintenanceMutationStatus.ValidationFailed, "Cancelled maintenance windows cannot be edited.");
        dbContext.Entry(original).Property(item => item.Version).OriginalValue = command.Version;
        var now = timeProvider.GetUtcNow();
        var scope = ToScope(original.Targets.Single());
        var before = ToAudit(original, scope, false);
        Cancel(original, access.UserId, now);
        var replacement = CreateWindow(create, access.UserId, now);
        dbContext.MaintenanceWindows.Add(replacement);
        try
        {
            await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Cancelled, before, ToAudit(original, scope, false), cancellationToken);
            await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Created, null, ToAudit(replacement, command.Scope, true), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MaintenanceMutationResult.Success(replacement.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail(MaintenanceMutationStatus.ConcurrencyConflict, "This maintenance window changed. Reload it before trying again.");
        }
    }

    public async Task<MaintenanceMutationResult> CancelAsync(CancelMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return Fail(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var window = await dbContext.MaintenanceWindows.Include(item => item.Targets).Include(item => item.Occurrences)
            .SingleOrDefaultAsync(item => item.Id == command.MaintenanceWindowId, cancellationToken);
        if (window is null) return Fail(MaintenanceMutationStatus.NotFound, "The maintenance window was not found.");
        if (window.DeletedAt is not null) return Fail(MaintenanceMutationStatus.ValidationFailed, "This maintenance window is already cancelled.");
        dbContext.Entry(window).Property(item => item.Version).OriginalValue = command.Version;
        var now = timeProvider.GetUtcNow();
        var scope = ToScope(window.Targets.Single());
        var before = ToAudit(window, scope, false);
        Cancel(window, access.UserId, now);
        try
        {
            await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Cancelled, before, ToAudit(window, scope, false), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MaintenanceMutationResult.Success(window.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail(MaintenanceMutationStatus.ConcurrencyConflict, "This maintenance window changed. Reload it before trying again.");
        }
    }

    private async Task<List<string>> ValidateAsync(CreateMaintenanceWindow command, CancellationToken token)
    {
        var errors = new List<string>();
        if (command.EndsAt <= command.StartsAt) errors.Add("The maintenance end must be after its start.");
        if (command.Reason.Trim().Length is 0 or > 500) errors.Add("A maintenance reason of up to 500 characters is required.");
        if (!IsValidTimezone(command.TimezoneId)) errors.Add("Select a valid IANA timezone identifier.");
        if (command.SuppressionPolicy is not (MaintenanceSuppressionPolicies.SuppressAll or MaintenanceSuppressionPolicies.None)) errors.Add("Select a valid notification suppression policy.");
        if (!await ScopeExistsAsync(command.Scope, token)) errors.Add("Select an active target for this maintenance window.");
        return errors;
    }

    private Task<bool> ScopeExistsAsync(MaintenanceScope scope, CancellationToken token) => scope.Kind switch
    {
        MaintenanceScopeKind.Client => dbContext.Clients.AnyAsync(item => item.Id == scope.TargetId && item.DeletedAt == null && item.IsActive, token),
        MaintenanceScopeKind.Website => dbContext.Websites.AnyAsync(item => item.Id == scope.TargetId && item.DeletedAt == null && item.IsEnabled, token),
        MaintenanceScopeKind.Environment => dbContext.Environments.AnyAsync(item => item.Id == scope.TargetId && item.DeletedAt == null && item.IsActive, token),
        MaintenanceScopeKind.Endpoint => dbContext.Endpoints.AnyAsync(item => item.Id == scope.TargetId && item.DeletedAt == null && item.IsEnabled, token),
        MaintenanceScopeKind.Monitor => dbContext.EndpointMonitors.AnyAsync(item => item.Id == scope.TargetId && item.DeletedAt == null && item.IsEnabled, token),
        _ => Task.FromResult(false)
    };

    private static MaintenanceWindow CreateWindow(CreateMaintenanceWindow command, Guid userId, DateTimeOffset now)
    {
        var window = new MaintenanceWindow { Id = Guid.NewGuid(), CreatedByUserId = userId, Reason = command.Reason.Trim(), TimezoneId = command.TimezoneId.Trim(), SuppressionPolicy = command.SuppressionPolicy, PauseEscalation = command.PauseEscalation, ContinueFailureCounter = command.ContinueFailureCounter, CreatedAt = now, UpdatedAt = now, UpdatedByUserId = userId, Version = 1 };
        window.Targets.Add(CreateTarget(command.Scope));
        window.Occurrences.Add(new MaintenanceOccurrence { Id = Guid.NewGuid(), MaintenanceWindowId = window.Id, StartsAt = command.StartsAt.ToUniversalTime(), EndsAt = command.EndsAt.ToUniversalTime(), CreatedAt = now });
        return window;
    }

    private static MaintenanceTarget CreateTarget(MaintenanceScope scope) => new() { Id = Guid.NewGuid(), ClientId = scope.Kind == MaintenanceScopeKind.Client ? scope.TargetId : null, WebsiteId = scope.Kind == MaintenanceScopeKind.Website ? scope.TargetId : null, EnvironmentId = scope.Kind == MaintenanceScopeKind.Environment ? scope.TargetId : null, EndpointId = scope.Kind == MaintenanceScopeKind.Endpoint ? scope.TargetId : null, EndpointMonitorId = scope.Kind == MaintenanceScopeKind.Monitor ? scope.TargetId : null };
    private static void Cancel(MaintenanceWindow window, Guid userId, DateTimeOffset now) { window.DeletedAt = now; window.DeletedByUserId = userId; window.UpdatedAt = now; window.UpdatedByUserId = userId; window.Version++; }
    private static MaintenanceScope ToScope(MaintenanceTarget target) => target.ClientId is { } id ? new(MaintenanceScopeKind.Client, id) : target.WebsiteId is { } websiteId ? new(MaintenanceScopeKind.Website, websiteId) : target.EnvironmentId is { } environmentId ? new(MaintenanceScopeKind.Environment, environmentId) : target.EndpointId is { } endpointId ? new(MaintenanceScopeKind.Endpoint, endpointId) : new(MaintenanceScopeKind.Monitor, target.EndpointMonitorId!.Value);
    private static MaintenanceAuditSnapshot ToAudit(MaintenanceWindow window, MaintenanceScope scope, bool reasonChanged) => new(window.Id, scope.Kind.ToString(), scope.TargetId, window.Occurrences.Single().StartsAt, window.Occurrences.Single().EndsAt, window.TimezoneId, window.SuppressionPolicy, window.PauseEscalation, window.ContinueFailureCounter, window.DeletedAt is not null, reasonChanged, window.Version);
    private static bool IsValidTimezone(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return false; try { _ = TimeZoneInfo.FindSystemTimeZoneById(value.Trim()); return value.Contains('/', StringComparison.Ordinal) || value.Equals("UTC", StringComparison.Ordinal); } catch (TimeZoneNotFoundException) { return false; } catch (InvalidTimeZoneException) { return false; } }
    private static MaintenanceMutationResult Fail(MaintenanceMutationStatus status, params IEnumerable<string> errors) => MaintenanceMutationResult.Failure(status, errors);
}

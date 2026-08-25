using WebHealth.Application;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Maintenance;
using WebHealth.Application.Registry;
using WebHealth.Domain.Maintenance;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceWindowService(ApplicationDbContext dbContext, IAuditTrailWriter auditTrail, MaintenanceSchedulingOptions options, TimeProvider timeProvider) : IMaintenanceWindowService
{
    public async Task<MaintenanceMutationResult> CreateAsync(CreateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return Fail(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        var errors = await ValidateAsync(command, cancellationToken);
        if (errors.Count > 0) return Fail(MaintenanceMutationStatus.ValidationFailed, errors);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var window = CreateWindow(command, access.UserId, now, options.HorizonDays);
        dbContext.MaintenanceWindows.Add(window);
        await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Created, null, ToAudit(window, command.Scope, true), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MaintenanceMutationResult.Success(window.Id);
    }

    public async Task<MaintenanceMutationResult> UpdateAsync(UpdateMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return Fail(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        var create = new CreateMaintenanceWindow(command.Scope, command.StartsAt, command.EndsAt, command.TimezoneId, command.Reason, command.SuppressionPolicy, command.PauseEscalation, command.ContinueFailureCounter, command.Recurrence);
        var errors = await ValidateAsync(create, cancellationToken);
        if (errors.Count > 0) return Fail(MaintenanceMutationStatus.ValidationFailed, errors);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var original = await dbContext.MaintenanceWindows.Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.Id == command.MaintenanceWindowId, cancellationToken);
        if (original is null) return Fail(MaintenanceMutationStatus.NotFound, "The maintenance window was not found.");
        if (original.DeletedAt is not null) return Fail(MaintenanceMutationStatus.ValidationFailed, "Cancelled maintenance windows cannot be edited.");
        dbContext.Entry(original).Property(item => item.Version).OriginalValue = command.Version;
        var now = timeProvider.GetUtcNow();
        var scope = ToScope(original.Targets.Single());
        var before = ToAudit(original, scope, false);
        Cancel(original, access.UserId, now);
        var replacement = CreateWindow(create, access.UserId, now, options.HorizonDays);
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
        var window = await dbContext.MaintenanceWindows.Include(item => item.Targets)
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

    public async Task<MaintenanceArchiveResult> ArchiveCompletedAsync(RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return MaintenanceArchiveResult.Failure(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var windows = await dbContext.MaintenanceWindows.Include(item => item.Targets)
            .Where(MaintenanceArchiveEligibility.Archivable(now))
            .ToListAsync(cancellationToken);
        try
        {
            foreach (var window in windows)
            {
                var scope = ToScope(window.Targets.Single());
                var before = ToAudit(window, scope, false);
                Archive(window, access.UserId, now);
                await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Archived, before, ToAudit(window, scope, false), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return MaintenanceArchiveResult.Success(windows.Count);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MaintenanceArchiveResult.Failure(MaintenanceMutationStatus.ConcurrencyConflict, "A maintenance window changed while it was being archived. Nothing was archived; try again.");
        }
    }

    public async Task<MaintenanceMutationResult> RestoreAsync(RestoreMaintenanceWindow command, RegistryAccessContext access, CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access)) return Fail(MaintenanceMutationStatus.Forbidden, "You cannot manage maintenance windows.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var window = await dbContext.MaintenanceWindows.Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.Id == command.MaintenanceWindowId, cancellationToken);
        if (window is null) return Fail(MaintenanceMutationStatus.NotFound, "The maintenance window was not found.");
        if (window.ArchivedAt is null) return Fail(MaintenanceMutationStatus.ValidationFailed, "This maintenance window is not archived.");
        dbContext.Entry(window).Property(item => item.Version).OriginalValue = command.Version;
        var now = timeProvider.GetUtcNow();
        var scope = ToScope(window.Targets.Single());
        var before = ToAudit(window, scope, false);
        window.ArchivedAt = null;
        window.UpdatedAt = now;
        window.UpdatedByUserId = access.UserId;
        window.Version++;
        try
        {
            await auditTrail.RecordMaintenanceMutationAsync(new(access.UserId, now), MaintenanceAuditAction.Restored, before, ToAudit(window, scope, false), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MaintenanceMutationResult.Success(window.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail(MaintenanceMutationStatus.ConcurrencyConflict, "This maintenance window changed. Reload it before trying again.");
        }
    }

    private async Task<List<ValidationError>> ValidateAsync(CreateMaintenanceWindow command, CancellationToken token)
    {
        var errors = new List<ValidationError>();
        if (command.EndsAt <= command.StartsAt)
        {
            errors.Add(ValidationError.For(
                "EndsAtUtc", "The end must be after the start. Move the end later, or the start earlier."));
        }
        else if ((command.EndsAt - command.StartsAt).Ticks % TimeSpan.TicksPerSecond != 0)
        {
            errors.Add(ValidationError.For(
                "EndsAtUtc", "Enter times to the second. Drop any fraction of a second from the start or end."));
        }

        var reasonLength = command.Reason.Trim().Length;
        if (reasonLength == 0)
        {
            errors.Add(ValidationError.For(
                "Reason", "Enter why this maintenance window exists. It is kept with the record."));
        }
        else if (reasonLength > 500)
        {
            errors.Add(ValidationError.For(
                "Reason", $"This reason is {reasonLength} characters. Shorten it to 500 or fewer."));
        }

        if (!IsValidTimezone(command.TimezoneId))
        {
            errors.Add(ValidationError.For(
                "TimezoneId",
                "Select a time zone from the list. It only changes how these times are shown; "
                + "they are stored in UTC either way."));
        }

        if (command.SuppressionPolicy is not (MaintenanceSuppressionPolicies.SuppressAll or MaintenanceSuppressionPolicies.None))
        {
            errors.Add(ValidationError.For(
                "SuppressionPolicy", "Select a notification policy from the list."));
        }

        if (!await ScopeExistsAsync(command.Scope, token))
        {
            errors.Add(ValidationError.For(
                "ScopeId",
                "Select an active target. A target that has been archived or disabled cannot take "
                + "a maintenance window."));
        }

        errors.AddRange(ValidateRecurrence(command));
        return errors;
    }

    /// <summary>
    /// BR-M05. The recurrence is the anchor occurrence's local wall-clock time repeated, so a
    /// weekly recurrence that excludes the anchor's own day would contradict its declared start.
    /// </summary>
    private static IEnumerable<ValidationError> ValidateRecurrence(CreateMaintenanceWindow command)
    {
        var recurrence = command.Recurrence;
        if (!MaintenanceRecurrencePatterns.IsSupported(recurrence.Pattern))
        {
            yield return ValidationError.For("RecurrencePattern", "Select a repeat option from the list.");
            yield break;
        }

        if (!MaintenanceRecurrencePatterns.IsRecurring(recurrence.Pattern))
        {
            if (recurrence.DaysOfWeekMask != MaintenanceDayOfWeekMask.Empty || recurrence.Until is not null)
            {
                yield return ValidationError.For(
                    "RecurrencePattern",
                    "This window does not repeat, so it cannot carry repeat days or a repeat-until "
                    + "date. Clear those, or choose a repeating option.");
            }

            yield break;
        }

        if (recurrence.Until is { } until && until <= command.StartsAt)
        {
            yield return ValidationError.For(
                "RecurrenceUntilUtc",
                "The repeat-until date is on or before the first occurrence, so nothing would ever "
                + "run. Move it later, or clear it to repeat indefinitely.");
        }

        if (command.EndsAt - command.StartsAt > TimeSpan.FromDays(1))
        {
            yield return ValidationError.For(
                "EndsAtUtc",
                "A repeating window cannot be longer than 24 hours, or its occurrences would "
                + "overlap. Shorten it, or make it a one-off.");
        }

        if (recurrence.Pattern == MaintenanceRecurrencePatterns.Daily)
        {
            if (recurrence.DaysOfWeekMask != MaintenanceDayOfWeekMask.Empty)
            {
                yield return ValidationError.For(
                    "RecurrenceDays",
                    "A daily window already covers every day, so individual days cannot be selected. "
                    + "Clear them, or change the repeat to weekly.");
            }

            yield break;
        }

        if (recurrence.DaysOfWeekMask == MaintenanceDayOfWeekMask.Empty
            || !MaintenanceDayOfWeekMask.IsValid(recurrence.DaysOfWeekMask))
        {
            yield return ValidationError.For(
                "RecurrenceDays", "Select at least one day for a weekly window.");
            yield break;
        }

        if (MaintenanceScheduleExpansion.TryFindTimeZone(command.TimezoneId.Trim(), out var timeZone)
            && !MaintenanceDayOfWeekMask.Includes(
                recurrence.DaysOfWeekMask,
                TimeZoneInfo.ConvertTime(command.StartsAt, timeZone).DayOfWeek))
        {
            yield return ValidationError.For(
                "RecurrenceDays",
                $"The first occurrence starts on a "
                + $"{TimeZoneInfo.ConvertTime(command.StartsAt, timeZone).DayOfWeek}, so that day must be "
                + "selected. Tick it, or move the start to a day you have selected.");
        }
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

    /// <summary>
    /// Builds the window from its schedule specification and materialises the first horizon of
    /// occurrences in the same transaction, so a window suppresses from the moment it is created
    /// rather than from the next expansion tick.
    /// </summary>
    private static MaintenanceWindow CreateWindow(CreateMaintenanceWindow command, Guid userId, DateTimeOffset now, int horizonDays)
    {
        var duration = command.EndsAt - command.StartsAt;
        var startsAt = MaintenanceRecurrencePatterns.IsRecurring(command.Recurrence.Pattern)
            && MaintenanceScheduleExpansion.TryFindTimeZone(command.TimezoneId.Trim(), out var anchorZone)
            ? MaintenanceRecurrence.Canonicalize(command.StartsAt, anchorZone)
            : command.StartsAt.ToUniversalTime();
        var window = new MaintenanceWindow
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = userId,
            Reason = command.Reason.Trim(),
            TimezoneId = command.TimezoneId.Trim(),
            SuppressionPolicy = command.SuppressionPolicy,
            ScheduleStartsAt = startsAt,
            ScheduleDurationSeconds = (int)duration.TotalSeconds,
            RecurrencePattern = command.Recurrence.Pattern,
            RecurrenceDaysOfWeek = command.Recurrence.DaysOfWeekMask,
            RecurrenceUntil = command.Recurrence.Until?.ToUniversalTime(),
            PauseEscalation = command.PauseEscalation,
            ContinueFailureCounter = command.ContinueFailureCounter,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = userId,
            Version = 1
        };
        window.Targets.Add(CreateTarget(command.Scope));

        // A one-off or already-started window must still materialise its declared occurrence, so
        // the horizon is never allowed to fall before the anchor.
        var horizon = MaxOf(now.AddDays(horizonDays), startsAt.AddTicks(1));
        if (MaintenanceRecurrencePatterns.IsRecurring(window.RecurrencePattern)) window.ExpandedThrough = horizon;
        // ValidateAsync has already resolved the timezone through the same lookup, so materialisation
        // cannot fail here; a window is never persisted without the occurrences it declares.
        MaintenanceScheduleExpansion.TryMaterialise(
            window, startsAt, horizon, now, new HashSet<DateTimeOffset>(), out var occurrences);
        foreach (var occurrence in occurrences)
        {
            window.Occurrences.Add(occurrence);
        }

        return window;
    }

    private static DateTimeOffset MaxOf(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static MaintenanceTarget CreateTarget(MaintenanceScope scope) => new() { Id = Guid.NewGuid(), ClientId = scope.Kind == MaintenanceScopeKind.Client ? scope.TargetId : null, WebsiteId = scope.Kind == MaintenanceScopeKind.Website ? scope.TargetId : null, EnvironmentId = scope.Kind == MaintenanceScopeKind.Environment ? scope.TargetId : null, EndpointId = scope.Kind == MaintenanceScopeKind.Endpoint ? scope.TargetId : null, EndpointMonitorId = scope.Kind == MaintenanceScopeKind.Monitor ? scope.TargetId : null };
    private static void Cancel(MaintenanceWindow window, Guid userId, DateTimeOffset now) { window.DeletedAt = now; window.DeletedByUserId = userId; window.UpdatedAt = now; window.UpdatedByUserId = userId; window.Version++; }
    private static void Archive(MaintenanceWindow window, Guid userId, DateTimeOffset now) { window.ArchivedAt = now; window.UpdatedAt = now; window.UpdatedByUserId = userId; window.Version++; }
    private static MaintenanceScope ToScope(MaintenanceTarget target) => target.ClientId is { } id ? new(MaintenanceScopeKind.Client, id) : target.WebsiteId is { } websiteId ? new(MaintenanceScopeKind.Website, websiteId) : target.EnvironmentId is { } environmentId ? new(MaintenanceScopeKind.Environment, environmentId) : target.EndpointId is { } endpointId ? new(MaintenanceScopeKind.Endpoint, endpointId) : new(MaintenanceScopeKind.Monitor, target.EndpointMonitorId!.Value);
    private static MaintenanceAuditSnapshot ToAudit(MaintenanceWindow window, MaintenanceScope scope, bool reasonChanged) => new(window.Id, scope.Kind.ToString(), scope.TargetId, window.ScheduleStartsAt, window.ScheduleStartsAt.AddSeconds(window.ScheduleDurationSeconds), window.TimezoneId, window.RecurrencePattern, window.RecurrenceDaysOfWeek, window.RecurrenceUntil, window.SuppressionPolicy, window.PauseEscalation, window.ContinueFailureCounter, window.DeletedAt is not null, reasonChanged, window.Version);
    private static bool IsValidTimezone(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return false; try { _ = TimeZoneInfo.FindSystemTimeZoneById(value.Trim()); return value.Contains('/', StringComparison.Ordinal) || value.Equals("UTC", StringComparison.Ordinal); } catch (TimeZoneNotFoundException) { return false; } catch (InvalidTimeZoneException) { return false; } }
    private static MaintenanceMutationResult Fail(MaintenanceMutationStatus status, params IEnumerable<ValidationError> errors) => MaintenanceMutationResult.Failure(status, errors);
}

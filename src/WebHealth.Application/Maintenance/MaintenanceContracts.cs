namespace WebHealth.Application.Maintenance;

public enum MaintenanceScopeKind { Client, Website, Environment, Endpoint, Monitor }
public sealed record MaintenanceScope(MaintenanceScopeKind Kind, Guid TargetId);
public sealed record MaintenanceRecurrenceSpec(string Pattern, int DaysOfWeekMask, DateTimeOffset? Until);
public sealed record CreateMaintenanceWindow(MaintenanceScope Scope, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimezoneId, string Reason, string SuppressionPolicy, bool PauseEscalation, bool ContinueFailureCounter, MaintenanceRecurrenceSpec Recurrence);
public sealed record UpdateMaintenanceWindow(Guid MaintenanceWindowId, long Version, MaintenanceScope Scope, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimezoneId, string Reason, string SuppressionPolicy, bool PauseEscalation, bool ContinueFailureCounter, MaintenanceRecurrenceSpec Recurrence);
public sealed record CancelMaintenanceWindow(Guid MaintenanceWindowId, long Version);
public enum MaintenanceMutationStatus { Succeeded, Forbidden, NotFound, ValidationFailed, ConcurrencyConflict }
public sealed record MaintenanceMutationResult(MaintenanceMutationStatus Status, Guid? MaintenanceWindowId, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == MaintenanceMutationStatus.Succeeded;
    public static MaintenanceMutationResult Success(Guid id) => new(MaintenanceMutationStatus.Succeeded, id, []);
    public static MaintenanceMutationResult Failure(MaintenanceMutationStatus status, params IEnumerable<string> errors) => new(status, null, errors.ToArray());
}
public sealed record MaintenanceWindowListItem(Guid Id, string ScopeLabel, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimezoneId, string SuppressionPolicy, bool PauseEscalation, bool IsCancelled, MaintenanceRecurrenceSpec Recurrence, DateTimeOffset? NextOccurrenceStartsAt, long Version);
public sealed record MaintenanceWindowDetails(Guid Id, MaintenanceScope Scope, string ScopeLabel, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimezoneId, string Reason, string SuppressionPolicy, bool PauseEscalation, bool ContinueFailureCounter, bool IsCancelled, MaintenanceRecurrenceSpec Recurrence, DateTimeOffset? NextOccurrenceStartsAt, int OccurrenceCount, long Version);
public sealed record MaintenanceScopeOption(MaintenanceScopeKind Kind, Guid Id, string Label);
public sealed record ActiveMaintenanceOccurrence(Guid OccurrenceId, string SuppressionPolicy, bool PauseEscalation, bool ContinueFailureCounter);

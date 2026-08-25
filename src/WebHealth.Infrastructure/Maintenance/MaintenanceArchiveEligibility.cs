using System.Linq.Expressions;
using WebHealth.Domain.Maintenance;

namespace WebHealth.Infrastructure.Maintenance;

internal static class MaintenanceArchiveEligibility
{
    public static Expression<Func<MaintenanceWindow, bool>> Archivable(DateTimeOffset now) =>
        window => window.ArchivedAt == null
            && (window.DeletedAt != null
                || (!window.Occurrences.Any(occurrence => occurrence.EndsAt > now)
                    && (window.RecurrencePattern == MaintenanceRecurrencePatterns.None
                        || (window.RecurrenceUntil != null && window.RecurrenceUntil <= now))));

    public static bool IsFinished(
        bool hasRemainingOccurrence,
        string recurrencePattern,
        DateTimeOffset? recurrenceUntil,
        DateTimeOffset now) =>
        !hasRemainingOccurrence
            && (recurrencePattern == MaintenanceRecurrencePatterns.None
                || (recurrenceUntil is { } until && until <= now));
}

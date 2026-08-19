using WebHealth.Domain.Maintenance;

namespace WebHealth.Infrastructure.Maintenance;

/// <summary>
/// Bridges a stored window's schedule specification to the pure domain expansion, so window
/// creation and the recurring expander materialise occurrences through exactly one code path.
/// </summary>
internal static class MaintenanceScheduleExpansion
{
    public static MaintenanceSchedule ToSchedule(MaintenanceWindow window) => new(
        window.ScheduleStartsAt,
        TimeSpan.FromSeconds(window.ScheduleDurationSeconds),
        window.RecurrencePattern,
        window.RecurrenceDaysOfWeek,
        window.RecurrenceUntil);

    public static bool TryFindTimeZone(string timezoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return true;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }

    /// <summary>
    /// Returns false when the window's timezone cannot be resolved on this host. The caller must
    /// treat that as an expansion failure rather than an empty expansion: a missing timezone is
    /// recoverable, and silently reporting "no occurrences" would leave the period unsuppressed.
    /// </summary>
    public static bool TryMaterialise(
        MaintenanceWindow window,
        DateTimeOffset fromUtc,
        DateTimeOffset horizonUtc,
        DateTimeOffset now,
        IReadOnlySet<DateTimeOffset> existingStarts,
        out IReadOnlyList<MaintenanceOccurrence> occurrences)
    {
        occurrences = [];
        if (!TryFindTimeZone(window.TimezoneId, out var timeZone)) return false;
        occurrences = MaintenanceRecurrence.Expand(ToSchedule(window), timeZone, fromUtc, horizonUtc)
            .Where(interval => !existingStarts.Contains(interval.StartsAt))
            .Select(interval => new MaintenanceOccurrence
            {
                Id = Guid.NewGuid(),
                MaintenanceWindowId = window.Id,
                StartsAt = interval.StartsAt,
                EndsAt = interval.EndsAt,
                CreatedAt = now
            })
            .ToArray();
        return true;
    }
}

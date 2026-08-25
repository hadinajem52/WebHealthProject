namespace WebHealth.Domain.Maintenance;

public static class MaintenanceRecurrencePatterns
{
    public const string None = "None";
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";

    public static bool IsSupported(string value) => value is None or Daily or Weekly;
    public static bool IsRecurring(string value) => value is Daily or Weekly;
}

public static class MaintenanceDayOfWeekMask
{
    public const int Empty = 0;
    public const int All = 127;

    public static int Of(DayOfWeek day) => 1 << (int)day;
    public static bool Includes(int mask, DayOfWeek day) => (mask & Of(day)) != 0;
    public static bool IsValid(int mask) => mask is >= Empty and <= All;
}

public sealed record MaintenanceSchedule(
    DateTimeOffset StartsAt,
    TimeSpan Duration,
    string Pattern,
    int DaysOfWeekMask,
    DateTimeOffset? Until);

public readonly record struct MaintenanceOccurrenceInterval(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public static class MaintenanceRecurrence
{
    public static IReadOnlyList<MaintenanceOccurrenceInterval> Expand(
        MaintenanceSchedule schedule,
        TimeZoneInfo timeZone,
        DateTimeOffset fromUtc,
        DateTimeOffset horizonUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (schedule.Duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(schedule));

        if (!MaintenanceRecurrencePatterns.IsRecurring(schedule.Pattern))
        {
            return schedule.StartsAt < horizonUtc && schedule.StartsAt >= fromUtc
                ? [new(schedule.StartsAt, schedule.StartsAt + schedule.Duration)]
                : [];
        }

        var anchorLocal = TimeZoneInfo.ConvertTime(schedule.StartsAt, timeZone).DateTime;
        var timeOfDay = anchorLocal.TimeOfDay;
        var upperBound = schedule.Until is { } until && until < horizonUtc ? until : horizonUtc;

        var cursor = anchorLocal.Date;
        var resumeLocal = TimeZoneInfo.ConvertTime(fromUtc, timeZone).DateTime.Date.AddDays(-1);
        if (resumeLocal > cursor) cursor = resumeLocal;

        var occurrences = new List<MaintenanceOccurrenceInterval>();
        while (true)
        {
            var nominal = cursor + timeOfDay;
            var startsAt = ResolveLocalStart(nominal, timeZone);
            cursor = cursor.AddDays(1);

            if (startsAt >= upperBound) break;
            if (startsAt < fromUtc || startsAt < schedule.StartsAt) continue;
            if (schedule.Pattern == MaintenanceRecurrencePatterns.Weekly
                && !MaintenanceDayOfWeekMask.Includes(schedule.DaysOfWeekMask, nominal.DayOfWeek))
            {
                continue;
            }

            occurrences.Add(new(startsAt, startsAt + schedule.Duration));
        }

        return occurrences;
    }

    public static DateTimeOffset Canonicalize(DateTimeOffset startsAt, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return ResolveLocalStart(TimeZoneInfo.ConvertTime(startsAt, timeZone).DateTime, timeZone);
    }

    public static DateTimeOffset ResolveLocalStart(DateTime nominalLocal, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = DateTime.SpecifyKind(nominalLocal, DateTimeKind.Unspecified);

        if (timeZone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(local, timeZone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        }

        if (timeZone.IsInvalidTime(local))
        {
            return new DateTimeOffset(local, timeZone.GetUtcOffset(local.AddDays(-1))).ToUniversalTime();
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}

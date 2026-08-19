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

/// <summary>
/// Pure expansion of a recurring maintenance schedule into concrete UTC occurrence intervals
/// (BR-M05). Recurrence is expressed as a local wall-clock time of day in the window's IANA
/// timezone; the daylight-saving rules are documented in
/// docs/phase-6/Recurring_Maintenance_Occurrences.md and implemented in <see cref="ResolveLocalStart"/>.
/// </summary>
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

        // Start a day early so a window whose local start resolves back across midnight is not
        // skipped when expansion resumes from a watermark; occurrences before fromUtc are dropped.
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

    /// <summary>
    /// The instant a recurrence anchor must be stored as: the same local wall-clock time, resolved
    /// by the same rule every later occurrence uses. An operator can declare an anchor on the
    /// second pass of an autumn-back ambiguous local time; storing that instant unchanged would
    /// make the anchor unreachable by expansion, which resolves that wall time to the earlier
    /// instant. Canonicalising keeps "the first materialised occurrence is the declared start" true.
    /// </summary>
    public static DateTimeOffset Canonicalize(DateTimeOffset startsAt, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return ResolveLocalStart(TimeZoneInfo.ConvertTime(startsAt, timeZone).DateTime, timeZone);
    }

    /// <summary>
    /// Resolves a nominal local wall-clock start to a single UTC instant. A time inside a
    /// spring-forward gap is shifted forward by the length of the gap; an autumn-back ambiguous
    /// time resolves to the earlier of its two instants. Both produce exactly one occurrence.
    /// </summary>
    public static DateTimeOffset ResolveLocalStart(DateTime nominalLocal, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = DateTime.SpecifyKind(nominalLocal, DateTimeKind.Unspecified);

        if (timeZone.IsAmbiguousTime(local))
        {
            // The largest ambiguous offset is the earliest instant for the same wall-clock time.
            return new DateTimeOffset(local, timeZone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        }

        if (timeZone.IsInvalidTime(local))
        {
            // Resolving with the pre-transition offset lands on the first instant at or after the
            // transition. A gap is far shorter than a day, so the same wall time on the previous
            // day always carries the pre-transition offset.
            return new DateTimeOffset(local, timeZone.GetUtcOffset(local.AddDays(-1))).ToUniversalTime();
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}

using FluentAssertions;
using WebHealth.Domain.Maintenance;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-M05. The daylight-saving rules under test are the ones written down in
/// docs/phase-6/Recurring_Maintenance_Occurrences.md: a nominal local start inside a spring-forward
/// gap shifts forward by the gap, an autumn-back ambiguous start resolves to the earlier of its two
/// instants, and either way exactly one occurrence exists for the transition day.
/// </summary>
public sealed class MaintenanceRecurrenceTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);

    // 2026-03-29: 02:00 local jumps to 03:00 (+01:00 -> +02:00). 02:30 does not exist.
    private static readonly DateTime SpringForwardDay = new(2026, 3, 29);

    // 2026-10-25: 03:00 local falls back to 02:00 (+02:00 -> +01:00). 02:30 happens twice.
    private static readonly DateTime AutumnBackDay = new(2026, 10, 25);

    [Fact]
    public void ResolveLocalStart_ShiftsANonExistentLocalStartForwardByTheGap()
    {
        var resolved = MaintenanceRecurrence.ResolveLocalStart(SpringForwardDay.AddMinutes(150), Berlin);

        resolved.Should().Be(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
        TimeZoneInfo.ConvertTime(resolved, Berlin).DateTime.Should().Be(SpringForwardDay.AddMinutes(210),
            "02:30 shifts forward by the one-hour gap to 03:30 local");
    }

    [Fact]
    public void ResolveLocalStart_ResolvesAnAmbiguousLocalStartToTheEarlierInstant()
    {
        var resolved = MaintenanceRecurrence.ResolveLocalStart(AutumnBackDay.AddMinutes(150), Berlin);

        resolved.Should().Be(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero),
            "the daylight (+02:00) reading is the earlier of the two instants");
    }

    [Fact]
    public void Expand_ProducesExactlyOneOccurrenceOnEachTransitionDay()
    {
        var occurrences = ExpandDailyHalfHourWindowAt0230(
            new DateTimeOffset(2026, 3, 27, 1, 30, 0, TimeSpan.Zero), days: 4);

        occurrences.Should().ContainSingle(occurrence =>
            occurrence.StartsAt == new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));

        var autumn = ExpandDailyHalfHourWindowAt0230(
            new DateTimeOffset(2026, 10, 23, 0, 30, 0, TimeSpan.Zero), days: 4);

        autumn.Should().ContainSingle(occurrence =>
            occurrence.StartsAt == new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Expand_KeepsOccurrenceDurationConstantAcrossTransitions()
    {
        var spring = ExpandDailyHalfHourWindowAt0230(
            new DateTimeOffset(2026, 3, 27, 1, 30, 0, TimeSpan.Zero), days: 4);
        var autumn = ExpandDailyHalfHourWindowAt0230(
            new DateTimeOffset(2026, 10, 23, 0, 30, 0, TimeSpan.Zero), days: 4);

        spring.Concat(autumn).Should().OnlyContain(occurrence => occurrence.EndsAt - occurrence.StartsAt == HalfHour);
    }

    [Fact]
    public void Expand_KeepsLocalWallClockTimeStableAcrossAnOffsetChange()
    {
        var occurrences = ExpandDailyHalfHourWindowAt0230(
            new DateTimeOffset(2026, 3, 27, 1, 30, 0, TimeSpan.Zero), days: 4);

        var afterTransition = occurrences.First(occurrence =>
            occurrence.StartsAt >= new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero));
        TimeZoneInfo.ConvertTime(afterTransition.StartsAt, Berlin).TimeOfDay.Should().Be(TimeSpan.FromMinutes(150),
            "a wall-clock recurrence stays at 02:30 local once the new offset is in force");
    }

    [Fact]
    public void Expand_YieldsMonotonicallyIncreasingDistinctStarts()
    {
        var occurrences = ExpandDailyHalfHourWindowAt0230(
            new DateTimeOffset(2026, 3, 27, 1, 30, 0, TimeSpan.Zero), days: 10);

        occurrences.Select(occurrence => occurrence.StartsAt).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Expand_RepeatsTheSameInstantsForTheSameRange()
    {
        var range = new DateTimeOffset(2026, 3, 27, 1, 30, 0, TimeSpan.Zero);

        ExpandDailyHalfHourWindowAt0230(range, days: 10)
            .Should().BeEquivalentTo(ExpandDailyHalfHourWindowAt0230(range, days: 10),
                "expansion is a pure function of the schedule and the range");
    }

    [Fact]
    public void Expand_ResumingFromAWatermarkYieldsOnlyTheNewTail()
    {
        var anchor = new DateTimeOffset(2026, 3, 27, 1, 30, 0, TimeSpan.Zero);
        var schedule = DailySchedule(anchor);
        var watermark = anchor.AddDays(5);

        var full = MaintenanceRecurrence.Expand(schedule, Berlin, anchor, anchor.AddDays(10));
        var head = MaintenanceRecurrence.Expand(schedule, Berlin, anchor, watermark);
        var tail = MaintenanceRecurrence.Expand(schedule, Berlin, watermark, anchor.AddDays(10));

        head.Concat(tail).Should().BeEquivalentTo(full, options => options.WithStrictOrdering());
        head.Should().NotIntersectWith(tail);
    }

    [Fact]
    public void Expand_WeeklyEmitsOnlySelectedDays()
    {
        var anchor = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero); // Monday
        var mask = MaintenanceDayOfWeekMask.Of(DayOfWeek.Monday) | MaintenanceDayOfWeekMask.Of(DayOfWeek.Thursday);
        var schedule = new MaintenanceSchedule(anchor, TimeSpan.FromHours(1),
            MaintenanceRecurrencePatterns.Weekly, mask, null);

        var occurrences = MaintenanceRecurrence.Expand(schedule, Berlin, anchor, anchor.AddDays(14));

        occurrences.Should().HaveCount(4);
        occurrences.Select(occurrence => TimeZoneInfo.ConvertTime(occurrence.StartsAt, Berlin).DayOfWeek)
            .Should().OnlyContain(day => day == DayOfWeek.Monday || day == DayOfWeek.Thursday);
    }

    [Fact]
    public void Expand_StopsAtTheRecurrenceBoundExclusively()
    {
        var anchor = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var until = anchor.AddDays(3);
        var schedule = DailySchedule(anchor) with { Until = until };

        var occurrences = MaintenanceRecurrence.Expand(schedule, Berlin, anchor, anchor.AddDays(30));

        occurrences.Should().HaveCount(3);
        occurrences.Should().OnlyContain(occurrence => occurrence.StartsAt < until);
    }

    [Fact]
    public void Expand_NeverEmitsAnOccurrenceBeforeTheAnchor()
    {
        var anchor = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);

        var occurrences = MaintenanceRecurrence.Expand(
            DailySchedule(anchor), Berlin, anchor.AddDays(-30), anchor.AddDays(3));

        occurrences.Should().HaveCount(3);
        occurrences[0].StartsAt.Should().Be(anchor);
    }

    [Fact]
    public void Expand_OneOffProducesExactlyTheDeclaredOccurrence()
    {
        var anchor = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var schedule = new MaintenanceSchedule(anchor, TimeSpan.FromHours(2),
            MaintenanceRecurrencePatterns.None, MaintenanceDayOfWeekMask.Empty, null);

        var occurrences = MaintenanceRecurrence.Expand(schedule, Berlin, anchor, anchor.AddDays(90));

        occurrences.Should().ContainSingle();
        occurrences[0].Should().Be(new MaintenanceOccurrenceInterval(anchor, anchor.AddHours(2)));
    }

    [Fact]
    public void Canonicalize_MovesAnAmbiguousAnchorOntoTheInstantExpansionWillProduce()
    {
        // 02:30 at the standard (+01:00) offset is the second pass of the ambiguous hour.
        var declaredSecondPass = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero);

        var canonical = MaintenanceRecurrence.Canonicalize(declaredSecondPass, Berlin);

        canonical.Should().Be(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Expand_MaterialisesTheDeclaredStartOfAnAmbiguousAnchorOnceCanonicalised()
    {
        var declaredSecondPass = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero);
        var canonical = MaintenanceRecurrence.Canonicalize(declaredSecondPass, Berlin);

        var occurrences = MaintenanceRecurrence.Expand(
            DailySchedule(canonical), Berlin, canonical, canonical.AddDays(3));

        occurrences.Should().HaveCount(3);
        occurrences[0].StartsAt.Should().Be(canonical, "the anchor day must materialise its declared start");
    }

    [Fact]
    public void Canonicalize_LeavesAnUnambiguousAnchorWhereItWasDeclared()
    {
        var declared = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

        MaintenanceRecurrence.Canonicalize(declared, Berlin).Should().Be(declared);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 1)]
    [InlineData(DayOfWeek.Wednesday, 8)]
    [InlineData(DayOfWeek.Saturday, 64)]
    public void DayOfWeekMask_MatchesTheStoredBitPositions(DayOfWeek day, int expected)
    {
        MaintenanceDayOfWeekMask.Of(day).Should().Be(expected);
        MaintenanceDayOfWeekMask.Includes(MaintenanceDayOfWeekMask.All, day).Should().BeTrue();
        MaintenanceDayOfWeekMask.Includes(MaintenanceDayOfWeekMask.Empty, day).Should().BeFalse();
    }

    private static MaintenanceSchedule DailySchedule(DateTimeOffset anchor) => new(
        anchor, HalfHour, MaintenanceRecurrencePatterns.Daily, MaintenanceDayOfWeekMask.Empty, null);

    private static IReadOnlyList<MaintenanceOccurrenceInterval> ExpandDailyHalfHourWindowAt0230(
        DateTimeOffset anchor, int days) =>
        MaintenanceRecurrence.Expand(DailySchedule(anchor), Berlin, anchor, anchor.AddDays(days));
}

using FluentAssertions;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MonitorCadenceTests
{
    [Theory]
    [InlineData(true, 300)]
    [InlineData(false, 900)]
    public void GetDefaultIntervalSeconds_UsesEnvironmentDefaults(
        bool isProduction,
        int expectedSeconds)
    {
        MonitorCadence.GetDefaultIntervalSeconds(isProduction).Should().Be(expectedSeconds);
    }

    [Fact]
    public void Initialize_MakesMonitorImmediatelyDueInUtc()
    {
        var localTime = new DateTimeOffset(2026, 8, 16, 15, 30, 0, TimeSpan.FromHours(3));

        var schedule = MonitorCadence.Initialize(localTime);

        schedule.Anchor.Should().Be(new DateTimeOffset(2026, 8, 16, 12, 30, 0, TimeSpan.Zero));
        schedule.NextDueAt.Should().Be(schedule.Anchor);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(1, 300)]
    [InlineData(299, 300)]
    [InlineData(300, 600)]
    [InlineData(901, 1200)]
    public void GetFirstSlotAfter_SkipsMissedIntervals(long elapsedSeconds, long expectedSeconds)
    {
        var anchor = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        var nextDueAt = MonitorCadence.GetFirstSlotAfter(
            anchor,
            intervalSeconds: 300,
            anchor.AddSeconds(elapsedSeconds));

        nextDueAt.Should().Be(anchor.AddSeconds(expectedSeconds));
    }

    [Fact]
    public void GetFirstSlotAfter_ReturnsFutureAnchorWhenScheduleHasNotStarted()
    {
        var anchor = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        MonitorCadence.GetFirstSlotAfter(anchor, 300, anchor.AddMinutes(-1)).Should().Be(anchor);
    }

    /// <summary>
    /// The defect this covers: resuming a monitor rejoined the grid with
    /// <see cref="MonitorCadence.GetFirstSlotAfter" />, which always returns a slot strictly after
    /// now. A daily certificate monitor resumed a minute after its slot therefore reported nothing
    /// for the next 23 hours and 59 minutes, and its dashboard row sat on Unknown.
    /// </summary>
    [Fact]
    public void GetResumeDueAt_ChecksImmediatelyWhenADueTimeWasMissed()
    {
        var slot = new DateTimeOffset(2026, 8, 20, 12, 30, 33, TimeSpan.Zero);
        var resumedAt = slot.AddSeconds(32);

        MonitorCadence.GetResumeDueAt(slot, resumedAt).Should().Be(resumedAt);
    }

    [Fact]
    public void GetResumeDueAt_CollapsesEveryMissedDueTimeIntoOneCheck()
    {
        var slot = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var resumedAt = slot.AddDays(3);

        MonitorCadence.GetResumeDueAt(slot, resumedAt).Should().Be(resumedAt);
    }

    /// <summary>
    /// A pause shorter than the remaining wait missed nothing, so it must not turn into an extra
    /// check the cadence never asked for.
    /// </summary>
    [Fact]
    public void GetResumeDueAt_KeepsAnUnreachedDueTime()
    {
        var slot = new DateTimeOffset(2026, 8, 20, 12, 30, 0, TimeSpan.Zero);

        MonitorCadence.GetResumeDueAt(slot, slot.AddMinutes(-2)).Should().Be(slot);
    }

    [Fact]
    public void GetResumeDueAt_NormalizesToUtc()
    {
        var slot = new DateTimeOffset(2026, 8, 20, 15, 30, 0, TimeSpan.FromHours(3));

        // Be compares instants, so the offset is asserted separately: next_due_at is stored as
        // timestamptz and every other cadence value is written in UTC.
        MonitorCadence.GetResumeDueAt(slot, slot.AddMinutes(-2))
            .Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CreateCadenceKey_NormalizesOffsetAndIsStable()
    {
        var monitorId = Guid.Parse("8d9e13d6-21a7-4c7e-9338-f6b8a76b3548");
        var utc = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var local = utc.ToOffset(TimeSpan.FromHours(3));

        MonitorCadence.CreateCadenceKey(monitorId, local)
            .Should().Be(MonitorCadence.CreateCadenceKey(monitorId, utc));
    }

    [Fact]
    public void GetFirstSlotAfter_RejectsNonPositiveInterval()
    {
        var now = DateTimeOffset.UtcNow;

        var act = () => MonitorCadence.GetFirstSlotAfter(now, 0, now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

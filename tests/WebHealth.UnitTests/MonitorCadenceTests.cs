using FluentAssertions;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MonitorCadenceTests
{
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

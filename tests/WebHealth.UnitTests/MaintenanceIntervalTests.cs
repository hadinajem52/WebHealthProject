using FluentAssertions;
using WebHealth.Domain.Maintenance;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MaintenanceIntervalTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    public void Contains_UsesStartInclusiveEndExclusiveBoundaries(int minutes, bool expected) =>
        MaintenanceInterval.Contains(Start, Start.AddHours(1), Start.AddMinutes(minutes)).Should().Be(expected);

    [Fact]
    public void Overlaps_ExcludesAdjacentIntervals() =>
        MaintenanceInterval.Overlaps(Start, Start.AddHours(1), Start.AddHours(1), Start.AddHours(2)).Should().BeFalse();
}

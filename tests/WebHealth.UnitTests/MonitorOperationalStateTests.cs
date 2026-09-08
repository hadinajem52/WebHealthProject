using FluentAssertions;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MonitorOperationalStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, false, false, "Disabled")]
    [InlineData(true, false, false, "ManualOnly")]
    [InlineData(true, true, false, "Paused")]
    [InlineData(true, true, true, "Delayed")]
    public void LifecycleAndSchedulingPrecedeFreshness(bool eligible, bool scheduled, bool enabled, string expected)
    {
        var result = MonitorOperationalState.Evaluate("Critical", eligible, scheduled, enabled, 300,
            Now.AddHours(-1), null, Now, TimeSpan.FromMinutes(10));

        result.State.Should().Be(expected);
        result.ConfirmedHealth.Should().Be("Critical");
    }

    [Theory]
    [InlineData(600, "NeverChecked")]
    [InlineData(601, "Delayed")]
    public void DelayStartsStrictlyAfterGrace(int overdueSeconds, string expected)
    {
        var result = MonitorOperationalState.Evaluate(null, true, true, true, 300,
            Now.AddSeconds(-overdueSeconds), null, Now, TimeSpan.FromMinutes(10));

        result.State.Should().Be(expected);
    }

    [Theory]
    [InlineData(300, 900, "Active")]
    [InlineData(300, 901, "Stale")]
    [InlineData(86400, 108000, "Active")]
    [InlineData(86400, 108001, "Stale")]
    public void FreshnessUsesTheLargerOfTenMinutesAndQuarterCadence(int interval, int age, string expected)
    {
        var result = MonitorOperationalState.Evaluate("Healthy", true, true, true, interval,
            Now.AddMinutes(1), Now.AddSeconds(-age), Now, TimeSpan.FromMinutes(10));

        result.State.Should().Be(expected);
        result.ConfirmedHealth.Should().Be("Healthy");
    }

    [Fact]
    public void LegacyDisabledHealthMapsToUnknownWithoutChangingOperationalState()
    {
        var result = MonitorOperationalState.Evaluate("Disabled", true, false, true, 300,
            Now, Now.AddDays(-1), Now, TimeSpan.FromMinutes(10));

        result.ConfirmedHealth.Should().Be("Unknown");
        result.State.Should().Be("ManualOnly");
        result.IsStale.Should().BeTrue();
    }
}

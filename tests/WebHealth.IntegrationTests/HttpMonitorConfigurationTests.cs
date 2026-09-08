using FluentAssertions;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class HttpMonitorConfigurationTests
{
    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"intervalSeconds\":600}", 600)]
    public void LegacyJsonPreservesMaterializedTimeout(string json, int? interval)
    {
        var overrides = HttpMonitorConfiguration.ReadOverrides(json, 30, 2, 2, 1500, 3000);

        overrides.TimeoutSeconds.Should().Be(30);
        overrides.IntervalSeconds.Should().Be(interval);
    }

    [Fact]
    public void UnversionedUnknownSettingsAreNotSilentlyIgnored()
    {
        FluentActions.Invoking(() => HttpMonitorConfiguration.ReadOverrides("{\"timeoutSecond\":20}", 30, 2, 2, 1500, 3000))
            .Should().Throw<InvalidOperationException>();
    }
}

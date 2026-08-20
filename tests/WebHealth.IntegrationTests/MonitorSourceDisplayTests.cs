using WebHealth.Application.Monitoring;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class MonitorSourceDisplayTests
{
    [Theory]
    [InlineData(HttpResultNormalizer.MonitorSource, "WebHealth HTTP monitor")]
    [InlineData(SslResultNormalizer.MonitorSource, "WebHealth certificate monitor")]
    [InlineData(null, "—")]
    public void Name_UsesReadableLabels(string? source, string expected)
    {
        Assert.Equal(expected, MonitorSourceDisplay.Name(source));
    }

    [Fact]
    public void Name_PreservesUnknownSources()
    {
        Assert.Equal("CustomMonitorV2", MonitorSourceDisplay.Name("CustomMonitorV2"));
    }
}

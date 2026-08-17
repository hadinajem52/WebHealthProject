using FluentAssertions;
using WebHealth.Infrastructure.Persistence.Migrations;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class HangfireSchemaResourcesTests
{
    [Fact]
    public void Assembly_EmbedsExactlyTheExpectedHangfireInstallScripts()
    {
        var assembly = typeof(HangfireSchedulingAndRecovery).Assembly;
        var expected = Enumerable.Range(3, 21)
            .Select(version => $"WebHealth.Infrastructure.Persistence.Migrations.HangfireSchema.Install.v{version}.sql");

        assembly.GetManifestResourceNames()
            .Where(name => name.Contains("HangfireSchema", StringComparison.Ordinal))
            .Should().BeEquivalentTo(expected);
    }
}

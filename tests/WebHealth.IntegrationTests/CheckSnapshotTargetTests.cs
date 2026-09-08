using FluentAssertions;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Registry;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class CheckSnapshotTargetTests
{
    [Fact]
    public void VersionTwoResolvesWithoutReadingTheCurrentRegistryTarget()
    {
        var check = CreateCheck();

        var target = CheckSnapshotTarget.Resolve(check);

        target.EndpointId.Should().Be(check.EndpointMonitor.EndpointId);
        target.NormalizedUrl.Should().Be("https://original.example.com:8443/health");
        target.NormalizedHost.Should().Be("original.example.com");
        target.EffectivePort.Should().Be(8443);
        target.NormalizationVersion.Should().Be(1);
        target.IsProduction.Should().BeTrue();
    }

    [Fact]
    public void LegacyVersionOneUsesTheExplicitRegistryCompatibilityPath()
    {
        var check = CreateCheck();
        check.ConfigurationSnapshot.SchemaVersion = 1;
        check.EndpointMonitor.Endpoint = new Endpoint
        {
            Id = check.EndpointMonitor.EndpointId,
            DisplayUrl = "http://legacy.example.com/health",
            NormalizedUrl = "http://legacy.example.com/health",
            NormalizedUrlHash = new byte[32],
            NormalizedHost = "legacy.example.com",
            EffectivePort = 80,
            NormalizationVersion = 1,
            Environment = new WebsiteEnvironment
            {
                Name = "Staging",
                NormalizedName = "staging",
                EnvironmentType = "Staging",
                IsProduction = false
            }
        };

        var target = CheckSnapshotTarget.Resolve(check);

        target.NormalizedUrl.Should().Be("http://legacy.example.com/health");
        target.EffectivePort.Should().Be(80);
        target.IsProduction.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void UnknownSnapshotVersionsFailClosed(short version)
    {
        var check = CreateCheck();
        check.ConfigurationSnapshot.SchemaVersion = version;

        FluentActions.Invoking(() => CheckSnapshotTarget.Resolve(check))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IncompleteVersionTwoDoesNotFallBackToTheRegistry()
    {
        var check = CreateCheck();
        check.ConfigurationSnapshot.TargetIsProduction = null;

        FluentActions.Invoking(() => CheckSnapshotTarget.Resolve(check))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConflictingSnapshotHostAndUrlFailBeforeExecution()
    {
        var check = CreateCheck();
        check.ConfigurationSnapshot.TargetNormalizedHost = "different.example.com";

        FluentActions.Invoking(() => CheckSnapshotTarget.Resolve(check))
            .Should().Throw<InvalidOperationException>();
    }

    private static LogicalCheck CreateCheck() => new()
    {
        Id = Guid.NewGuid(),
        Source = "Scheduled",
        State = "Queued",
        PolicyFingerprint = new string('a', 64),
        EndpointMonitor = new EndpointMonitor
        {
            EndpointId = Guid.NewGuid(),
            MonitorType = "HttpAvailability",
            BoundedOverrides = "{}",
            ConfigurationFingerprint = new string('a', 64)
        },
        ConfigurationSnapshot = new CheckConfigurationSnapshot
        {
            SchemaVersion = 2,
            TargetNormalizedUrl = "https://original.example.com:8443/health",
            TargetNormalizedHost = "original.example.com",
            TargetEffectivePort = 8443,
            TargetNormalizationVersion = 1,
            TargetIsProduction = true,
            CurrentTruthGeneration = 2,
            MonitorType = "HttpAvailability",
            ConfigurationFingerprint = new string('a', 64),
            IntervalSource = "SystemDefault",
            TimeoutSource = "SystemDefault",
            ConfirmationSource = "SystemDefault",
            ThresholdSource = "SystemDefault"
        }
    };
}

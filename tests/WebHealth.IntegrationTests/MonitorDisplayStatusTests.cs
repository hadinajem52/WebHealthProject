using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Registry;
using WebHealth.Infrastructure.Reporting;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class MonitorDisplayStatusTests
{
    public static TheoryData<bool, bool, string?> Combinations()
    {
        var data = new TheoryData<bool, bool, string?>();
        foreach (var monitorEnabled in new[] { true, false })
        {
            foreach (var endpointEnabled in new[] { true, false })
            {
                foreach (var status in new string?[]
                {
                    null,
                    EndpointHealthStatuses.Healthy,
                    EndpointHealthStatuses.Warning,
                    EndpointHealthStatuses.Critical,
                    EndpointHealthStatuses.Unknown
                })
                {
                    data.Add(monitorEnabled, endpointEnabled, status);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void ProjectionAgreesWithTheRule(bool monitorEnabled, bool endpointEnabled, string? confirmedStatus)
    {
        var monitor = Monitor(monitorEnabled, endpointEnabled, confirmedStatus);

        MonitorDisplayStatus.Projection.Compile()(monitor)
            .Should().Be(MonitorDisplayStatus.Of(monitorEnabled, endpointEnabled, confirmedStatus));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void FilterSelectsExactlyWhatTheProjectionReports(
        bool monitorEnabled, bool endpointEnabled, string? confirmedStatus)
    {
        var monitor = Monitor(monitorEnabled, endpointEnabled, confirmedStatus);
        var reported = MonitorDisplayStatus.Projection.Compile()(monitor);

        foreach (var candidate in new[]
        {
            EndpointHealthStatuses.Healthy,
            EndpointHealthStatuses.Warning,
            EndpointHealthStatuses.Critical,
            EndpointHealthStatuses.Unknown,
            EndpointHealthStatuses.Disabled
        })
        {
            MonitorDisplayStatus.Matches(candidate).Compile()(monitor)
                .Should().Be(candidate == reported,
                    $"a monitor reported as {reported} must be selected by {candidate} only when they match");
        }
    }

    [Theory]
    [InlineData(EndpointHealthStatuses.Healthy)]
    [InlineData(EndpointHealthStatuses.Warning)]
    [InlineData(EndpointHealthStatuses.Critical)]
    [InlineData(null)]
    public void ADisabledMonitorNeverReportsItsLastState(string? lastConfirmed) =>
        MonitorDisplayStatus.Projection.Compile()(
            Monitor(monitorEnabled: false, endpointEnabled: true, lastConfirmed))
            .Should().Be(EndpointHealthStatuses.Disabled);

    [Theory]
    [InlineData(EndpointHealthStatuses.Healthy)]
    [InlineData(EndpointHealthStatuses.Warning)]
    [InlineData(EndpointHealthStatuses.Critical)]
    [InlineData(null)]
    public void AMonitorOnADisabledEndpointNeverReportsItsLastState(string? lastConfirmed) =>
        MonitorDisplayStatus.Projection.Compile()(
            Monitor(monitorEnabled: true, endpointEnabled: false, lastConfirmed))
            .Should().Be(EndpointHealthStatuses.Disabled);

    [Fact]
    public void AnEnabledMonitorWithNoConfirmationIsUnknown() =>
        MonitorDisplayStatus.Projection.Compile()(
            Monitor(monitorEnabled: true, endpointEnabled: true, null))
            .Should().Be(EndpointHealthStatuses.Unknown);

    private static EndpointMonitor Monitor(bool monitorEnabled, bool endpointEnabled, string? confirmedStatus) => new()
    {
        Id = Guid.NewGuid(),
        IsEnabled = monitorEnabled,
        BoundedOverrides = "{}",
        ConfigurationFingerprint = string.Empty,
        MonitorType = HttpIssueIdentity.MonitorType,
        Endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            IsEnabled = endpointEnabled,
            DisplayUrl = "https://display-status.test/",
            NormalizedUrl = "https://display-status.test/",
            NormalizedUrlHash = new byte[32],
            NormalizedHost = "display-status.test",
            Environment = new WebsiteEnvironment
            {
                Id = Guid.NewGuid(),
                Name = "Production",
                NormalizedName = "PRODUCTION",
                EnvironmentType = "Production",
                IsActive = true,
                Website = new Website
                {
                    Id = Guid.NewGuid(),
                    Name = "Display status",
                    NormalizedName = "DISPLAY STATUS",
                    IsEnabled = true,
                    Client = new Client
                    {
                        Id = Guid.NewGuid(),
                        Name = "Client",
                        NormalizedName = "CLIENT",
                        IsActive = true
                    }
                }
            }
        },
        EndpointHealth = confirmedStatus is null
            ? null
            : new EndpointHealth { ConfirmedStatus = confirmedStatus }
    };
}

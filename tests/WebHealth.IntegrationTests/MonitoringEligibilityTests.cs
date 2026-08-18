using FluentAssertions;
using WebHealth.Infrastructure.Registry;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Pausing a monitor must stop scheduled dispatch without blocking on-demand runs.
/// The predicates are evaluated in memory here so the split stays covered without a database.
/// </summary>
public sealed class MonitoringEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PausedMonitor_IsNotScheduled_ButStaysTestable()
    {
        var endpoints = new[] { CreateEndpoint(monitorEnabled: false) }.AsQueryable();

        MonitoringEligibility.Apply(endpoints, Now).Should().BeEmpty();
        MonitoringEligibility.ApplyTestable(endpoints, Now).Should().ContainSingle();
    }

    [Fact]
    public void ActiveMonitor_IsBothScheduledAndTestable()
    {
        var endpoints = new[] { CreateEndpoint(monitorEnabled: true) }.AsQueryable();

        MonitoringEligibility.Apply(endpoints, Now).Should().ContainSingle();
        MonitoringEligibility.ApplyTestable(endpoints, Now).Should().ContainSingle();
    }

    [Fact]
    public void ManualOnlyEndpoint_IsNotScheduled_ButStaysTestable()
    {
        var endpoint = CreateEndpoint(monitorEnabled: true);
        endpoint.Monitors.Single().SchedulingEnabled = false;
        var endpoints = new[] { endpoint }.AsQueryable();

        MonitoringEligibility.Apply(endpoints, Now).Should().BeEmpty();
        MonitoringEligibility.ApplyTestable(endpoints, Now).Should().ContainSingle();
    }

    [Fact]
    public void DisabledEndpoint_IsNeitherScheduledNorTestable()
    {
        var endpoint = CreateEndpoint(monitorEnabled: true);
        endpoint.IsEnabled = false;
        var endpoints = new[] { endpoint }.AsQueryable();

        MonitoringEligibility.Apply(endpoints, Now).Should().BeEmpty();
        MonitoringEligibility.ApplyTestable(endpoints, Now).Should().BeEmpty();
    }

    [Fact]
    public void MissingAuthorizationEvidence_IsNeitherScheduledNorTestable()
    {
        var endpoint = CreateEndpoint(monitorEnabled: true);
        endpoint.TargetAuthorizations.Clear();
        var endpoints = new[] { endpoint }.AsQueryable();

        MonitoringEligibility.Apply(endpoints, Now).Should().BeEmpty();
        MonitoringEligibility.ApplyTestable(endpoints, Now).Should().BeEmpty();
    }

    [Fact]
    public void DeletedMonitor_IsNeitherScheduledNorTestable()
    {
        var endpoint = CreateEndpoint(monitorEnabled: true);
        endpoint.Monitors.Single().DeletedAt = Now;
        var endpoints = new[] { endpoint }.AsQueryable();

        MonitoringEligibility.Apply(endpoints, Now).Should().BeEmpty();
        MonitoringEligibility.ApplyTestable(endpoints, Now).Should().BeEmpty();
    }

    private static Endpoint CreateEndpoint(bool monitorEnabled)
    {
        var client = new Client { Name = "Client", NormalizedName = "client", IsActive = true };
        var website = new Website
        {
            Name = "Website",
            NormalizedName = "website",
            IsEnabled = true,
            Client = client
        };
        var environment = new WebsiteEnvironment
        {
            Name = "Production",
            NormalizedName = "production",
            EnvironmentType = "Production",
            IsProduction = true,
            IsActive = true,
            Website = website
        };
        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            DisplayUrl = "https://example.test/",
            NormalizedUrl = "https://example.test/",
            NormalizedUrlHash = new byte[32],
            NormalizedHost = "example.test",
            EffectivePort = 443,
            IsEnabled = true,
            Environment = environment
        };
        endpoint.Monitors.Add(new EndpointMonitor
        {
            Id = Guid.NewGuid(),
            MonitorType = "HttpAvailability",
            BoundedOverrides = "{}",
            ConfigurationFingerprint = "fingerprint",
            SchedulingEnabled = true,
            IsEnabled = monitorEnabled
        });
        endpoint.TargetAuthorizations.Add(new TargetAuthorizationEvidence
        {
            Id = Guid.NewGuid(),
            AuthorizationKind = "Owned",
            EvidenceReference = "domain owned by me",
            NormalizedHost = "example.test",
            Port = 443,
            EffectiveFrom = Now.AddDays(-1)
        });
        return endpoint;
    }
}

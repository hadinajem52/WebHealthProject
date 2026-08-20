using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Registry;
using WebHealth.Infrastructure.Reporting;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// The dashboard needs the same status rule in three query shapes: the row, the status filter and
/// the health totals. Entity Framework cannot compose one stored expression inside another, so the
/// rule is written more than once — and a change made to one form but not the others would let the
/// page contradict itself, showing a monitor in a status its own filter excludes.
/// <para>
/// The expressions are compiled and run in memory here, so the agreement is checked without a
/// database.
/// </para>
/// </summary>
public sealed class MonitorDisplayStatusTests
{
    public static TheoryData<bool, bool, string?> Combinations()
    {
        var data = new TheoryData<bool, bool, string?>();
        foreach (var monitorEnabled in new[] { true, false })
        {
            // Both switches, because either one being off means nothing is being checked. The
            // endpoint dimension is the one that used to be missing here, which is exactly why
            // the rule could read the monitor alone without a test noticing.
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

    /// <summary>
    /// The filter must select exactly the monitors the row would show in that status — no more,
    /// because a monitor would then appear under a status it is not in, and no fewer, because
    /// filtering for a status that is on screen would hide it.
    /// </summary>
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

    /// <summary>
    /// The point of the change: a disabled monitor is never reported as the state it was in when
    /// checking stopped, whatever that state was.
    /// </summary>
    [Theory]
    [InlineData(EndpointHealthStatuses.Healthy)]
    [InlineData(EndpointHealthStatuses.Warning)]
    [InlineData(EndpointHealthStatuses.Critical)]
    [InlineData(null)]
    public void ADisabledMonitorNeverReportsItsLastState(string? lastConfirmed) =>
        MonitorDisplayStatus.Projection.Compile()(
            Monitor(monitorEnabled: false, endpointEnabled: true, lastConfirmed))
            .Should().Be(EndpointHealthStatuses.Disabled);

    /// <summary>
    /// Disabling the endpoint stops dispatch just as surely as pausing the monitor does -
    /// <c>MonitoringEligibility</c> requires both - so the dashboard must not keep reporting the
    /// last confirmed state of an endpoint somebody switched off.
    /// </summary>
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

    // Only the three fields the rule reads matter; the rest are required members of the entities.
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
            NormalizedHost = "display-status.test"
        },
        EndpointHealth = confirmedStatus is null
            ? null
            : new EndpointHealth { ConfirmedStatus = confirmedStatus }
    };
}

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
    public static TheoryData<bool, string?> Combinations()
    {
        var data = new TheoryData<bool, string?>();
        foreach (var isEnabled in new[] { true, false })
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
                data.Add(isEnabled, status);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void ProjectionAgreesWithTheRule(bool isEnabled, string? confirmedStatus)
    {
        var monitor = Monitor(isEnabled, confirmedStatus);

        MonitorDisplayStatus.Projection.Compile()(monitor)
            .Should().Be(MonitorDisplayStatus.Of(isEnabled, confirmedStatus));
    }

    /// <summary>
    /// The filter must select exactly the monitors the row would show in that status — no more,
    /// because a monitor would then appear under a status it is not in, and no fewer, because
    /// filtering for a status that is on screen would hide it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Combinations))]
    public void FilterSelectsExactlyWhatTheProjectionReports(bool isEnabled, string? confirmedStatus)
    {
        var monitor = Monitor(isEnabled, confirmedStatus);
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
        MonitorDisplayStatus.Projection.Compile()(Monitor(isEnabled: false, lastConfirmed))
            .Should().Be(EndpointHealthStatuses.Disabled);

    [Fact]
    public void AnEnabledMonitorWithNoConfirmationIsUnknown() =>
        MonitorDisplayStatus.Projection.Compile()(Monitor(isEnabled: true, null))
            .Should().Be(EndpointHealthStatuses.Unknown);

    // Only the two fields the rule reads matter; the rest are required members of the entity.
    private static EndpointMonitor Monitor(bool isEnabled, string? confirmedStatus) => new()
    {
        Id = Guid.NewGuid(),
        IsEnabled = isEnabled,
        BoundedOverrides = "{}",
        ConfigurationFingerprint = string.Empty,
        MonitorType = HttpIssueIdentity.MonitorType,
        EndpointHealth = confirmedStatus is null
            ? null
            : new EndpointHealth { ConfirmedStatus = confirmedStatus }
    };
}

using FluentAssertions;
using WebHealth.Application.Incidents;
using WebHealth.Domain.Incidents;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class IncidentLifecycleEngineTests
{
    [Theory]
    [InlineData(IncidentStatuses.Open, IncidentLifecycleAction.Acknowledge, IncidentStatuses.Acknowledged)]
    [InlineData(IncidentStatuses.Acknowledged, IncidentLifecycleAction.StartProgress, IncidentStatuses.InProgress)]
    [InlineData(IncidentStatuses.Open, IncidentLifecycleAction.BeginRecovery, IncidentStatuses.MonitoringRecovery)]
    [InlineData(IncidentStatuses.MonitoringRecovery, IncidentLifecycleAction.ConfirmRecovery, IncidentStatuses.Resolved)]
    [InlineData(IncidentStatuses.Resolved, IncidentLifecycleAction.Close, IncidentStatuses.Closed)]
    public void ValidTransitions_AreAccepted(string current, IncidentLifecycleAction action, string expected)
    {
        var decision = IncidentLifecycleEngine.Evaluate(new(current, action));

        decision.Succeeded.Should().BeTrue();
        decision.NewStatus.Should().Be(expected);
    }

    [Fact]
    public void RecoveryFailure_ReturnsToTheCorrectInvestigationState()
    {
        IncidentLifecycleEngine.Evaluate(new(
                IncidentStatuses.MonitoringRecovery,
                IncidentLifecycleAction.InterruptRecovery,
                WasAcknowledged: false))
            .NewStatus.Should().Be(IncidentStatuses.Open);
        IncidentLifecycleEngine.Evaluate(new(
                IncidentStatuses.MonitoringRecovery,
                IncidentLifecycleAction.InterruptRecovery,
                WasAcknowledged: true))
            .NewStatus.Should().Be(IncidentStatuses.InProgress);
    }

    [Theory]
    [InlineData(null, "note")]
    [InlineData("category", null)]
    [InlineData("", "note")]
    [InlineData("category", "")]
    public void ManualResolution_RequiresCategoryAndNote(string? category, string? note)
    {
        IncidentLifecycleEngine.Evaluate(new(
                IncidentStatuses.Open,
                IncidentLifecycleAction.ResolveManually,
                Category: category,
                NoteOrReason: note))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public void NormalClosure_IsRejectedBeforeResolution()
    {
        IncidentLifecycleEngine.Evaluate(new(
                IncidentStatuses.InProgress,
                IncidentLifecycleAction.Close))
            .Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(IncidentLifecycleAction.ForceClose, IncidentStatuses.Open)]
    [InlineData(IncidentLifecycleAction.Reopen, IncidentStatuses.Closed)]
    public void AdministratorOverride_RequiresAdministratorAndReason(
        IncidentLifecycleAction action,
        string currentStatus)
    {
        IncidentLifecycleEngine.Evaluate(new(currentStatus, action, true, NoteOrReason: "reason"))
            .Succeeded.Should().BeTrue();
        IncidentLifecycleEngine.Evaluate(new(currentStatus, action, false, NoteOrReason: "reason"))
            .Succeeded.Should().BeFalse();
        IncidentLifecycleEngine.Evaluate(new(currentStatus, action, true, NoteOrReason: ""))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RecurrenceWindow_IncludesTheExactThirtyDayBoundary()
    {
        var closedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        IncidentLifecycleEngine.IsWithinRecurrenceWindow(closedAt, closedAt.AddDays(30))
            .Should().BeTrue();
        IncidentLifecycleEngine.IsWithinRecurrenceWindow(closedAt, closedAt.AddDays(30).AddTicks(1))
            .Should().BeFalse();
        IncidentLifecycleEngine.IsWithinRecurrenceWindow(closedAt, closedAt.AddTicks(-1))
            .Should().BeFalse();
    }

    [Fact]
    public void Duration_IsBoundedAtZeroForOutOfOrderEvidence()
    {
        var now = DateTimeOffset.UtcNow;

        IncidentLifecycleEngine.DurationMilliseconds(now, now.AddMilliseconds(123.1)).Should().Be(124);
        IncidentLifecycleEngine.DurationMilliseconds(now, now.AddSeconds(-1)).Should().Be(0);
    }
}

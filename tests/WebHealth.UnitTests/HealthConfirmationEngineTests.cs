using FluentAssertions;
using WebHealth.Application.Health;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class HealthConfirmationEngineTests
{
    private const string IssueKey = "v1|HttpAvailability|Http.ServerError|default";

    [Fact]
    public void TwoConsecutiveFailures_ConfirmCritical()
    {
        var first = Evaluate(EndpointHealthStatuses.Healthy, [], [IssueKey], false);
        first.ConfirmedStatus.Should().BeNull();
        first.Issues.Single().ConsecutiveFailures.Should().Be(1);

        var second = Evaluate(EndpointHealthStatuses.Healthy, first.Issues, [IssueKey], false);
        second.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Critical);
        second.Transition.Should().Be(HealthTransition.FailureConfirmed);
    }

    [Fact]
    public void FailPassFail_RestartsFailureConfirmation()
    {
        var firstFailure = Evaluate(EndpointHealthStatuses.Healthy, [], [IssueKey], false);
        var pass = Evaluate(EndpointHealthStatuses.Healthy, firstFailure.Issues, [], true);
        var secondFailure = Evaluate(EndpointHealthStatuses.Healthy, pass.Issues, [IssueKey], false);

        pass.Issues.Single().ConsecutiveFailures.Should().Be(0);
        secondFailure.Issues.Single().ConsecutiveFailures.Should().Be(1);
        secondFailure.ConfirmedStatus.Should().BeNull();
    }

    [Fact]
    public void TwoPasses_ConfirmRecovery_OnlyOnSecondPass()
    {
        var current = new[] { new HealthIssueCounter(IssueKey, 2, 0) };

        var first = Evaluate(EndpointHealthStatuses.Critical, current, [], true);
        var second = Evaluate(EndpointHealthStatuses.Critical, first.Issues, [], true);

        first.ConfirmedStatus.Should().BeNull();
        first.Transition.Should().Be(HealthTransition.RecoveryStarted);
        second.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Healthy);
        second.Transition.Should().Be(HealthTransition.RecoveryConfirmed);
    }

    [Fact]
    public void InitialPass_ConfirmsHealthyImmediately()
    {
        var decision = Evaluate(EndpointHealthStatuses.Unknown, [], [], true);

        decision.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Healthy);
        decision.Transition.Should().Be(HealthTransition.InitialHealthy);
    }

    [Theory]
    [InlineData(HealthCounterMode.Ignore, 1)]
    [InlineData(HealthCounterMode.Reset, 0)]
    public void ExcludedPolicies_DoNotAdvanceFailureConfirmation(HealthCounterMode mode, int expectedFailures)
    {
        var current = new[] { new HealthIssueCounter(IssueKey, 1, 0) };

        var decision = Evaluate(EndpointHealthStatuses.Healthy, current, [IssueKey], false, mode);

        decision.Issues.Single().ConsecutiveFailures.Should().Be(expectedFailures);
        decision.ConfirmedStatus.Should().BeNull();
        decision.Transition.Should().Be(HealthTransition.None);
    }

    [Theory]
    [InlineData(LogicalCheckSources.Manual, HttpResultOutcomes.Critical, null, false, false, HealthCounterMode.Ignore)]
    [InlineData(LogicalCheckSources.Scheduled, HttpResultOutcomes.Cancelled, null, false, false, HealthCounterMode.Ignore)]
    [InlineData(LogicalCheckSources.Scheduled, HttpResultOutcomes.Critical, HttpFailureCategories.TargetIneligible, false, false, HealthCounterMode.Ignore)]
    [InlineData(LogicalCheckSources.Scheduled, HttpResultOutcomes.Critical, null, true, false, HealthCounterMode.Reset)]
    [InlineData(LogicalCheckSources.Scheduled, HttpResultOutcomes.Critical, null, true, true, HealthCounterMode.Count)]
    [InlineData(LogicalCheckSources.Scheduled, HttpResultOutcomes.Critical, null, false, false, HealthCounterMode.Count)]
    public void CounterMode_EnforcesSourceAndMaintenancePolicy(
        string source,
        string outcome,
        string? failureCategory,
        bool isMaintenance,
        bool continueFailureCounter,
        HealthCounterMode expected)
    {
        HealthConfirmationEngine.SelectCounterMode(
            source, outcome, failureCategory, isMaintenance, continueFailureCounter)
            .Should().Be(expected);
    }

    private static HealthConfirmationDecision Evaluate(
        string status,
        IReadOnlyCollection<HealthIssueCounter> issues,
        IReadOnlyCollection<string> observed,
        bool isPassing,
        HealthCounterMode mode = HealthCounterMode.Count) =>
        HealthConfirmationEngine.Evaluate(new(
            status, issues, observed, isPassing, 2, 2, mode));
}

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
    private const string ExpiryIssueKey = "v1|SslCertificate|Ssl.ExpiringSoon|abc123";

    private static readonly string SlowResponseIssueKey =
        HttpIssueIdentity.Create(PerformanceRules.SlowResponse);

    private static readonly string PageSizeIssueKey =
        HttpIssueIdentity.Create(PerformanceRules.PageTooLarge);

    [Fact]
    public void TwoConsecutiveFailures_ConfirmCritical()
    {
        var first = Evaluate(EndpointHealthStatuses.Healthy, [], [Critical(IssueKey)], false);
        first.ConfirmedStatus.Should().BeNull();
        first.Issues.Single().ConsecutiveFailures.Should().Be(1);

        var second = Evaluate(EndpointHealthStatuses.Healthy, first.Issues, [Critical(IssueKey)], false);
        second.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Critical);
        second.Transition.Should().Be(HealthTransition.FailureConfirmed);
    }

    [Fact]
    public void FailPassFail_RestartsFailureConfirmation()
    {
        var firstFailure = Evaluate(EndpointHealthStatuses.Healthy, [], [Critical(IssueKey)], false);
        var pass = Evaluate(EndpointHealthStatuses.Healthy, firstFailure.Issues, [], true);
        var secondFailure = Evaluate(EndpointHealthStatuses.Healthy, pass.Issues, [Critical(IssueKey)], false);

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

        var decision = Evaluate(EndpointHealthStatuses.Healthy, current, [Critical(IssueKey)], false, mode);

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

    [Fact]
    public void ConfirmedWarningIssue_ConfirmsWarningRatherThanCritical()
    {
        // BR-C04: an endpoint whose certificate expires in 30 days is still serving traffic.
        var first = Evaluate(
            EndpointHealthStatuses.Healthy, [], [Warning(ExpiryIssueKey)], false);
        var second = Evaluate(
            EndpointHealthStatuses.Healthy, first.Issues, [Warning(ExpiryIssueKey)], false);

        second.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Warning);
        second.Transition.Should().Be(HealthTransition.FailureConfirmed);
        second.ConfirmedIssueKeys.Should().ContainSingle().Which.Should().Be(ExpiryIssueKey);
    }

    [Fact]
    public void HighSeverityIssue_ConfirmsWarningStatus()
    {
        // High is an escalation of urgency, not of unavailability, so it stops at Warning.
        var first = Evaluate(
            EndpointHealthStatuses.Healthy, [], [High(ExpiryIssueKey)], false);
        var second = Evaluate(
            EndpointHealthStatuses.Healthy, first.Issues, [High(ExpiryIssueKey)], false);

        second.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Warning);
    }

    [Fact]
    public void CriticalIssue_EscalatesAnAlreadyConfirmedWarning()
    {
        var current = new[]
        {
            new HealthIssueCounter(ExpiryIssueKey, 2, 0),
            new HealthIssueCounter(IssueKey, 1, 0)
        };

        var decision = Evaluate(
            EndpointHealthStatuses.Warning,
            current,
            [Warning(ExpiryIssueKey), Critical(IssueKey)],
            false);

        decision.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Critical);
        decision.Transition.Should().Be(HealthTransition.FailureConfirmed);
        decision.ConfirmedIssueKeys.Should().BeEquivalentTo([ExpiryIssueKey, IssueKey]);
    }

    [Fact]
    public void ConfirmedWarning_RecoversLikeAConfirmedCritical()
    {
        var current = new[] { new HealthIssueCounter(ExpiryIssueKey, 2, 0) };

        var first = Evaluate(EndpointHealthStatuses.Warning, current, [], true);
        var second = Evaluate(EndpointHealthStatuses.Warning, first.Issues, [], true);

        first.Transition.Should().Be(HealthTransition.RecoveryStarted);
        second.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Healthy);
        second.Transition.Should().Be(HealthTransition.RecoveryConfirmed);
    }

    [Fact]
    public void SlowResponse_ConfirmsOnItsOwnCountWhileAvailabilityConfirmsOnTheMonitorCount()
    {
        // BR-P03 against a monitor that confirms availability in two: the slow-response issue
        // still needs three consecutive breaches, and the availability issue still needs two.
        var observed = new[] { Critical(IssueKey), SlowResponse() };

        var first = Evaluate(EndpointHealthStatuses.Healthy, [], observed, false);
        var second = Evaluate(EndpointHealthStatuses.Healthy, first.Issues, observed, false);
        var third = Evaluate(EndpointHealthStatuses.Critical, second.Issues, observed, false);

        first.ConfirmedIssueKeys.Should().BeEmpty();
        second.ConfirmedIssueKeys.Should().ContainSingle().Which.Should().Be(IssueKey);
        third.ConfirmedIssueKeys.Should().BeEquivalentTo([IssueKey, SlowResponseIssueKey]);
    }

    [Fact]
    public void SlowResponse_ResetsOnASampleThatIsNotSlow()
    {
        // BR-P03: two breaches, then a fast sample that still failed for another reason. The
        // slow-response counter restarts; the availability counter does not.
        var slowAndFailing = new[] { Critical(IssueKey), SlowResponse() };

        var first = Evaluate(EndpointHealthStatuses.Healthy, [], slowAndFailing, false);
        var second = Evaluate(EndpointHealthStatuses.Healthy, first.Issues, slowAndFailing, false);
        var fastButFailing = Evaluate(
            EndpointHealthStatuses.Critical, second.Issues, [Critical(IssueKey)], false);
        var slowAgain = Evaluate(
            EndpointHealthStatuses.Critical, fastButFailing.Issues, slowAndFailing, false);

        fastButFailing.Issues.Single(issue => issue.IssueKey == SlowResponseIssueKey)
            .ConsecutiveFailures.Should().Be(0);
        slowAgain.Issues.Single(issue => issue.IssueKey == SlowResponseIssueKey)
            .ConsecutiveFailures.Should().Be(1);
        slowAgain.ConfirmedIssueKeys.Should().NotContain(SlowResponseIssueKey);
    }

    [Fact]
    public void SlowResponse_ResetsOnAPassingSample()
    {
        var slow = new[] { SlowResponse() };
        var first = Evaluate(EndpointHealthStatuses.Healthy, [], slow, false);
        var second = Evaluate(EndpointHealthStatuses.Healthy, first.Issues, slow, false);
        var passing = Evaluate(EndpointHealthStatuses.Healthy, second.Issues, [], true);

        passing.Issues.Single().ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void AnIssueRecoversWhileAnotherIssueOnTheSameEndpointKeepsFailing()
    {
        // The defect this guards: a page-size warning on every sample means the endpoint never
        // produces a wholly healthy result, so an availability incident could never resolve.
        var current = new[]
        {
            new HealthIssueCounter(IssueKey, 2, 0),
            new HealthIssueCounter(PageSizeIssueKey, 2, 0)
        };
        var pageSizeOnly = new[] { Warning(PageSizeIssueKey) };

        var first = Evaluate(EndpointHealthStatuses.Critical, current, pageSizeOnly, false);
        var second = Evaluate(EndpointHealthStatuses.Critical, first.Issues, pageSizeOnly, false);

        first.RecoveredIssueKeys.Should().BeEmpty();
        second.RecoveredIssueKeys.Should().ContainSingle().Which.Should().Be(IssueKey);
        second.RecoveredIssueKeys.Should().NotContain(PageSizeIssueKey);
    }

    [Fact]
    public void ARecoveringIssue_RestartsItsRecoveryCountWhenItFailsAgain()
    {
        var current = new[]
        {
            new HealthIssueCounter(IssueKey, 0, 1),
            new HealthIssueCounter(PageSizeIssueKey, 2, 0)
        };

        var relapse = Evaluate(
            EndpointHealthStatuses.Critical,
            current,
            [Critical(IssueKey), Warning(PageSizeIssueKey)],
            false);

        relapse.Issues.Single(issue => issue.IssueKey == IssueKey)
            .ConsecutiveRecoveries.Should().Be(0);
        relapse.RecoveredIssueKeys.Should().BeEmpty();
    }

    [Fact]
    public void AHealthyEndpointDoesNotAccumulateRecoveryCredit()
    {
        // Nothing is recovering from a Healthy endpoint, so a failing sample must not hand
        // unrelated issues a head start toward resolution.
        var current = new[] { new HealthIssueCounter(IssueKey, 0, 0) };

        var decision = Evaluate(
            EndpointHealthStatuses.Healthy, current, [Warning(PageSizeIssueKey)], false);

        decision.Issues.Single(issue => issue.IssueKey == IssueKey)
            .ConsecutiveRecoveries.Should().Be(0);
        decision.RecoveredIssueKeys.Should().BeEmpty();
    }

    [Fact]
    public void AnObservedIssueWithAnInvalidSeverity_IsRejected()
    {
        var act = () => Evaluate(
            EndpointHealthStatuses.Healthy, [], [new ObservedIssue(IssueKey, "Nuisance", 1)], false);

        act.Should().Throw<ArgumentException>();
    }

    private static ObservedIssue Critical(string issueKey) =>
        new(issueKey, FindingSeverities.Critical, 2);

    private static ObservedIssue High(string issueKey) => new(issueKey, FindingSeverities.High, 2);

    private static ObservedIssue Warning(string issueKey) =>
        new(issueKey, FindingSeverities.Warning, 2);

    private static ObservedIssue SlowResponse() => new(
        SlowResponseIssueKey,
        FindingSeverities.Warning,
        PerformanceRules.SelectFailureConfirmationCount(PerformanceRules.SlowResponse, 2));

    private static HealthConfirmationDecision Evaluate(
        string status,
        IReadOnlyCollection<HealthIssueCounter> issues,
        IReadOnlyCollection<ObservedIssue> observed,
        bool isPassing,
        HealthCounterMode mode = HealthCounterMode.Count) =>
        HealthConfirmationEngine.Evaluate(new(status, issues, observed, isPassing, 2, mode));
}

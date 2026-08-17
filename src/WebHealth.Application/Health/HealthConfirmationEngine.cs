using WebHealth.Domain.Health;
using WebHealth.Domain.Monitoring;
using WebHealth.Application.Monitoring;

namespace WebHealth.Application.Health;

public enum HealthCounterMode
{
    Ignore,
    Reset,
    Count
}

public enum HealthTransition
{
    None,
    InitialHealthy,
    FailureConfirmed,
    RecoveryStarted,
    RecoveryConfirmed
}

public sealed record HealthIssueCounter(
    string IssueKey,
    int ConsecutiveFailures,
    int ConsecutiveRecoveries);

public sealed record EvaluateHealthConfirmation(
    string CurrentStatus,
    IReadOnlyCollection<HealthIssueCounter> CurrentIssues,
    IReadOnlyCollection<string> ObservedIssueKeys,
    bool IsPassing,
    int FailureConfirmationCount,
    int RecoveryConfirmationCount,
    HealthCounterMode CounterMode);

public sealed record HealthConfirmationDecision(
    IReadOnlyList<HealthIssueCounter> Issues,
    string? ConfirmedStatus,
    HealthTransition Transition);

public static class HealthConfirmationEngine
{
    public static HealthCounterMode SelectCounterMode(
        string source,
        string outcome,
        string? failureCategory,
        bool isMaintenance,
        bool continueFailureCounter)
    {
        if (source != LogicalCheckSources.Scheduled
            || outcome == HttpResultOutcomes.Cancelled
            || failureCategory == HttpFailureCategories.TargetIneligible)
        {
            return HealthCounterMode.Ignore;
        }

        return isMaintenance && !continueFailureCounter
            ? HealthCounterMode.Reset
            : HealthCounterMode.Count;
    }

    public static HealthConfirmationDecision Evaluate(EvaluateHealthConfirmation input)
    {
        Validate(input);

        if (input.CounterMode == HealthCounterMode.Ignore)
        {
            return Unchanged(input);
        }

        if (input.CounterMode == HealthCounterMode.Reset)
        {
            return new(input.CurrentIssues.Select(Reset).ToArray(), null, HealthTransition.None);
        }

        return input.IsPassing ? EvaluatePass(input) : EvaluateFailure(input);
    }

    private static HealthConfirmationDecision EvaluatePass(EvaluateHealthConfirmation input)
    {
        var isRecovering = input.CurrentStatus == EndpointHealthStatuses.Critical;
        var issues = input.CurrentIssues.Select(issue => new HealthIssueCounter(
            issue.IssueKey,
            0,
            isRecovering ? Increment(issue.ConsecutiveRecoveries) : 0)).ToArray();

        if (!isRecovering)
        {
            var status = input.CurrentStatus == EndpointHealthStatuses.Unknown
                ? EndpointHealthStatuses.Healthy
                : null;
            return new(issues, status, status is null
                ? HealthTransition.None
                : HealthTransition.InitialHealthy);
        }

        var recoveryCount = issues.Select(issue => issue.ConsecutiveRecoveries).DefaultIfEmpty(1).Min();
        return recoveryCount >= input.RecoveryConfirmationCount
            ? new(issues, EndpointHealthStatuses.Healthy, HealthTransition.RecoveryConfirmed)
            : new(issues, null, HealthTransition.RecoveryStarted);
    }

    private static HealthConfirmationDecision EvaluateFailure(EvaluateHealthConfirmation input)
    {
        var observed = input.ObservedIssueKeys.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var current = input.CurrentIssues.ToDictionary(issue => issue.IssueKey, StringComparer.Ordinal);
        var issues = current.Keys.Union(observed, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(issueKey => observed.Contains(issueKey)
                ? IncrementFailure(issueKey, current.GetValueOrDefault(issueKey))
                : new HealthIssueCounter(issueKey, 0, 0))
            .ToArray();
        var isConfirmed = issues.Any(issue =>
            observed.Contains(issue.IssueKey)
            && issue.ConsecutiveFailures >= input.FailureConfirmationCount);

        return isConfirmed && input.CurrentStatus != EndpointHealthStatuses.Critical
            ? new(issues, EndpointHealthStatuses.Critical, HealthTransition.FailureConfirmed)
            : new(issues, null, HealthTransition.None);
    }

    private static HealthIssueCounter IncrementFailure(string issueKey, HealthIssueCounter? current) =>
        new(issueKey, Increment(current?.ConsecutiveFailures ?? 0), 0);

    private static HealthIssueCounter Reset(HealthIssueCounter issue) => new(issue.IssueKey, 0, 0);

    private static HealthConfirmationDecision Unchanged(EvaluateHealthConfirmation input) =>
        new(input.CurrentIssues.ToArray(), null, HealthTransition.None);

    private static int Increment(int value) => value == int.MaxValue ? value : value + 1;

    private static void Validate(EvaluateHealthConfirmation input)
    {
        if (input.FailureConfirmationCount <= 0
            || input.RecoveryConfirmationCount <= 0
            || input.CurrentIssues.Any(issue => issue.ConsecutiveFailures < 0
                || issue.ConsecutiveRecoveries < 0
                || string.IsNullOrWhiteSpace(issue.IssueKey))
            || input.CurrentIssues.Select(issue => issue.IssueKey).Distinct(StringComparer.Ordinal).Count()
                != input.CurrentIssues.Count
            || input.ObservedIssueKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The health confirmation input is invalid.", nameof(input));
        }
    }
}

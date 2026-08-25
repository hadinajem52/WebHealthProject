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

public sealed record ObservedIssue(
    string IssueKey,
    string Severity,
    int FailureConfirmationCount);

public sealed record EvaluateHealthConfirmation(
    string CurrentStatus,
    IReadOnlyCollection<HealthIssueCounter> CurrentIssues,
    IReadOnlyCollection<ObservedIssue> ObservedIssues,
    IReadOnlyCollection<string> IndeterminateIssueKeys,
    bool IsPassing,
    int RecoveryConfirmationCount,
    HealthCounterMode CounterMode);

public sealed record HealthConfirmationDecision(
    IReadOnlyList<HealthIssueCounter> Issues,
    IReadOnlyList<string> ConfirmedIssueKeys,
    IReadOnlyList<string> RecoveryStartedIssueKeys,
    IReadOnlyList<string> RecoveredIssueKeys,
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

    public static string ToHealthStatus(string severity) =>
        severity == FindingSeverities.Critical
            ? EndpointHealthStatuses.Critical
            : EndpointHealthStatuses.Warning;

    public static HealthConfirmationDecision Evaluate(EvaluateHealthConfirmation input)
    {
        Validate(input);

        if (input.CounterMode == HealthCounterMode.Ignore)
        {
            return Unchanged(input);
        }

        if (input.CounterMode == HealthCounterMode.Reset)
        {
            return new(input.CurrentIssues.Select(Reset).ToArray(), [], [], [], null, HealthTransition.None);
        }

        return input.IsPassing ? EvaluatePass(input) : EvaluateFailure(input);
    }

    private static HealthConfirmationDecision EvaluatePass(EvaluateHealthConfirmation input)
    {
        var isRecovering = IsUnhealthy(input.CurrentStatus);
        var indeterminate = input.IndeterminateIssueKeys.ToHashSet(StringComparer.Ordinal);
        var issues = input.CurrentIssues.Select(issue => indeterminate.Contains(issue.IssueKey)
            ? issue
            : new HealthIssueCounter(
                issue.IssueKey,
                0,
                isRecovering ? Increment(issue.ConsecutiveRecoveries) : 0)).ToArray();

        if (!isRecovering)
        {
            var status = input.CurrentStatus == EndpointHealthStatuses.Unknown
                ? EndpointHealthStatuses.Healthy
                : null;
            return new(issues, [], [], [], status, status is null
                ? HealthTransition.None
                : HealthTransition.InitialHealthy);
        }

        var recoveryStarted = SelectRecoveryStarted(
            issues, [], indeterminate, input.RecoveryConfirmationCount, isRecovering);
        var recovered = SelectRecovered(
            issues, [], indeterminate, input.RecoveryConfirmationCount);
        var recoveryCount = issues.Select(issue => issue.ConsecutiveRecoveries).DefaultIfEmpty(1).Min();
        return recoveryCount >= input.RecoveryConfirmationCount
            ? new(issues, [], recoveryStarted, recovered,
                EndpointHealthStatuses.Healthy, HealthTransition.RecoveryConfirmed)
            : new(issues, [], recoveryStarted, recovered, null,
                recoveryStarted.Count > 0 ? HealthTransition.RecoveryStarted : HealthTransition.None);
    }

    private static HealthConfirmationDecision EvaluateFailure(EvaluateHealthConfirmation input)
    {
        var observed = input.ObservedIssues
            .DistinctBy(issue => issue.IssueKey, StringComparer.Ordinal)
            .ToDictionary(issue => issue.IssueKey, StringComparer.Ordinal);
        var current = input.CurrentIssues.ToDictionary(issue => issue.IssueKey, StringComparer.Ordinal);
        var indeterminate = input.IndeterminateIssueKeys.ToHashSet(StringComparer.Ordinal);
        var wasUnhealthy = IsUnhealthy(input.CurrentStatus);
        var issues = current.Keys.Union(observed.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(issueKey => observed.ContainsKey(issueKey)
                ? IncrementFailure(issueKey, current.GetValueOrDefault(issueKey))
                : indeterminate.Contains(issueKey)
                    ? current[issueKey]
                    : IncrementRecovery(issueKey, current.GetValueOrDefault(issueKey), wasUnhealthy))
            .ToArray();

        var confirmed = issues
            .Where(issue => observed.TryGetValue(issue.IssueKey, out var observation)
                && issue.ConsecutiveFailures >= observation.FailureConfirmationCount)
            .Select(issue => issue.IssueKey)
            .ToArray();
        var confirmedStatus = confirmed
            .Select(issueKey => ToHealthStatus(observed[issueKey].Severity))
            .OrderByDescending(StatusRank)
            .FirstOrDefault();

        var recoveryStarted = SelectRecoveryStarted(
            issues, observed.Keys, indeterminate, input.RecoveryConfirmationCount, wasUnhealthy);
        var recovered = SelectRecovered(
            issues, observed.Keys, indeterminate, input.RecoveryConfirmationCount);
        return confirmedStatus is null || confirmedStatus == input.CurrentStatus
            ? new(issues, confirmed, recoveryStarted, recovered, null, HealthTransition.None)
            : new(issues, confirmed, recoveryStarted, recovered,
                confirmedStatus, HealthTransition.FailureConfirmed);
    }

    private static IReadOnlyList<string> SelectRecoveryStarted(
        IReadOnlyList<HealthIssueCounter> issues,
        IReadOnlyCollection<string> observedIssueKeys,
        IReadOnlySet<string> indeterminateIssueKeys,
        int recoveryConfirmationCount,
        bool wasUnhealthy) =>
        wasUnhealthy
            ? issues
                .Where(issue => !observedIssueKeys.Contains(issue.IssueKey, StringComparer.Ordinal)
                    && !indeterminateIssueKeys.Contains(issue.IssueKey)
                    && issue.ConsecutiveRecoveries == 1
                    && issue.ConsecutiveRecoveries < recoveryConfirmationCount)
                .Select(issue => issue.IssueKey)
                .ToArray()
            : [];

    private static IReadOnlyList<string> SelectRecovered(
        IReadOnlyList<HealthIssueCounter> issues,
        IReadOnlyCollection<string> observedIssueKeys,
        IReadOnlySet<string> indeterminateIssueKeys,
        int recoveryConfirmationCount) =>
        issues
            .Where(issue => !observedIssueKeys.Contains(issue.IssueKey, StringComparer.Ordinal)
                && !indeterminateIssueKeys.Contains(issue.IssueKey)
                && issue.ConsecutiveRecoveries >= recoveryConfirmationCount)
            .Select(issue => issue.IssueKey)
            .ToArray();

    private static bool IsUnhealthy(string status) =>
        status is EndpointHealthStatuses.Warning or EndpointHealthStatuses.Critical;

    private static int StatusRank(string status) =>
        status == EndpointHealthStatuses.Critical ? 2 : 1;

    private static HealthIssueCounter IncrementFailure(string issueKey, HealthIssueCounter? current) =>
        new(issueKey, Increment(current?.ConsecutiveFailures ?? 0), 0);

    private static HealthIssueCounter IncrementRecovery(
        string issueKey,
        HealthIssueCounter? current,
        bool wasUnhealthy) =>
        new(issueKey, 0, wasUnhealthy ? Increment(current?.ConsecutiveRecoveries ?? 0) : 0);

    private static HealthIssueCounter Reset(HealthIssueCounter issue) => new(issue.IssueKey, 0, 0);

    private static HealthConfirmationDecision Unchanged(EvaluateHealthConfirmation input) =>
        new(input.CurrentIssues.ToArray(), [], [], [], null, HealthTransition.None);

    private static int Increment(int value) => value == int.MaxValue ? value : value + 1;

    private static void Validate(EvaluateHealthConfirmation input)
    {
        if (input.RecoveryConfirmationCount <= 0
            || input.IndeterminateIssueKeys.Any(string.IsNullOrWhiteSpace)
            || input.IndeterminateIssueKeys.Distinct(StringComparer.Ordinal).Count()
                != input.IndeterminateIssueKeys.Count
            || input.CurrentIssues.Any(issue => issue.ConsecutiveFailures < 0
                || issue.ConsecutiveRecoveries < 0
                || string.IsNullOrWhiteSpace(issue.IssueKey))
            || input.CurrentIssues.Select(issue => issue.IssueKey).Distinct(StringComparer.Ordinal).Count()
                != input.CurrentIssues.Count
            || input.ObservedIssues.Any(issue => string.IsNullOrWhiteSpace(issue.IssueKey)
                || issue.FailureConfirmationCount <= 0
                || !FindingSeverities.All.Contains(issue.Severity))
            || input.ObservedIssues.Any(issue => input.IndeterminateIssueKeys.Contains(
                issue.IssueKey, StringComparer.Ordinal)))
        {
            throw new ArgumentException("The health confirmation input is invalid.", nameof(input));
        }
    }
}

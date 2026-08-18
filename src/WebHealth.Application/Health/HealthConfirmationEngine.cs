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

/// <summary>
/// One issue the current result observed, with the two things that decide what it does: how
/// severe it is, and how many consecutive samples it needs before it counts as confirmed.
/// The confirmation count is per issue rather than per monitor because BR-P03 gives slow
/// response its own three-breach rule while availability keeps the monitor's own.
/// </summary>
public sealed record ObservedIssue(
    string IssueKey,
    string Severity,
    int FailureConfirmationCount);

public sealed record EvaluateHealthConfirmation(
    string CurrentStatus,
    IReadOnlyCollection<HealthIssueCounter> CurrentIssues,
    IReadOnlyCollection<ObservedIssue> ObservedIssues,
    bool IsPassing,
    int RecoveryConfirmationCount,
    HealthCounterMode CounterMode);

public sealed record HealthConfirmationDecision(
    IReadOnlyList<HealthIssueCounter> Issues,
    IReadOnlyList<string> ConfirmedIssueKeys,
    /// <summary>
    /// Issues this result did <em>not</em> observe and which have now passed for long enough to
    /// count as recovered. Reported separately from <see cref="ConfirmedStatus" /> because
    /// recovery is per issue: an availability failure can clear while a page-size warning on the
    /// same endpoint persists, and the availability incident has to be allowed to resolve.
    /// </summary>
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

    /// <summary>
    /// The confirmed endpoint status a finding severity implies. <c>High</c> lands on
    /// <c>Warning</c> for the same reason it produces a warning outcome: the endpoint status is
    /// an availability signal, and a certificate expiring in fifteen days does not make the
    /// site unreachable.
    /// </summary>
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
            return new(input.CurrentIssues.Select(Reset).ToArray(), [], [], null, HealthTransition.None);
        }

        return input.IsPassing ? EvaluatePass(input) : EvaluateFailure(input);
    }

    private static HealthConfirmationDecision EvaluatePass(EvaluateHealthConfirmation input)
    {
        var isRecovering = IsUnhealthy(input.CurrentStatus);
        var issues = input.CurrentIssues.Select(issue => new HealthIssueCounter(
            issue.IssueKey,
            0,
            isRecovering ? Increment(issue.ConsecutiveRecoveries) : 0)).ToArray();

        if (!isRecovering)
        {
            var status = input.CurrentStatus == EndpointHealthStatuses.Unknown
                ? EndpointHealthStatuses.Healthy
                : null;
            return new(issues, [], [], status, status is null
                ? HealthTransition.None
                : HealthTransition.InitialHealthy);
        }

        var recovered = SelectRecovered(issues, [], input.RecoveryConfirmationCount);
        var recoveryCount = issues.Select(issue => issue.ConsecutiveRecoveries).DefaultIfEmpty(1).Min();
        return recoveryCount >= input.RecoveryConfirmationCount
            ? new(issues, [], recovered, EndpointHealthStatuses.Healthy, HealthTransition.RecoveryConfirmed)
            : new(issues, [], recovered, null, HealthTransition.RecoveryStarted);
    }

    private static HealthConfirmationDecision EvaluateFailure(EvaluateHealthConfirmation input)
    {
        var observed = input.ObservedIssues
            .DistinctBy(issue => issue.IssueKey, StringComparer.Ordinal)
            .ToDictionary(issue => issue.IssueKey, StringComparer.Ordinal);
        var current = input.CurrentIssues.ToDictionary(issue => issue.IssueKey, StringComparer.Ordinal);
        var wasUnhealthy = IsUnhealthy(input.CurrentStatus);
        var issues = current.Keys.Union(observed.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(issueKey => observed.ContainsKey(issueKey)
                ? IncrementFailure(issueKey, current.GetValueOrDefault(issueKey))
                // An issue this result did not observe is passing, even though some other issue
                // on the same endpoint failed. Its recovery counter therefore advances here too
                // — otherwise a lingering page-size warning would hold an unrelated availability
                // incident open indefinitely, because the endpoint never produces a wholly
                // healthy result again.
                : IncrementRecovery(issueKey, current.GetValueOrDefault(issueKey), wasUnhealthy))
            .ToArray();

        // An issue confirms on its own count (BR-P03), so a slow-response issue needing three
        // breaches and an availability issue needing two advance independently on one result.
        var confirmed = issues
            .Where(issue => observed.TryGetValue(issue.IssueKey, out var observation)
                && issue.ConsecutiveFailures >= observation.FailureConfirmationCount)
            .Select(issue => issue.IssueKey)
            .ToArray();
        var confirmedStatus = confirmed
            .Select(issueKey => ToHealthStatus(observed[issueKey].Severity))
            .OrderByDescending(StatusRank)
            .FirstOrDefault();

        var recovered = SelectRecovered(issues, observed.Keys, input.RecoveryConfirmationCount);
        return confirmedStatus is null || confirmedStatus == input.CurrentStatus
            ? new(issues, confirmed, recovered, null, HealthTransition.None)
            : new(issues, confirmed, recovered, confirmedStatus, HealthTransition.FailureConfirmed);
    }

    private static IReadOnlyList<string> SelectRecovered(
        IReadOnlyList<HealthIssueCounter> issues,
        IReadOnlyCollection<string> observedIssueKeys,
        int recoveryConfirmationCount) =>
        issues
            .Where(issue => !observedIssueKeys.Contains(issue.IssueKey, StringComparer.Ordinal)
                && issue.ConsecutiveRecoveries >= recoveryConfirmationCount)
            .Select(issue => issue.IssueKey)
            .ToArray();

    /// <summary>
    /// Warning and Critical are both "confirmed unhealthy" states, so recovery counting starts
    /// from either. Unknown is not: an endpoint that has never reported has nothing to recover
    /// from, and its first pass is an initial reading rather than a recovery.
    /// </summary>
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
        new(input.CurrentIssues.ToArray(), [], [], null, HealthTransition.None);

    private static int Increment(int value) => value == int.MaxValue ? value : value + 1;

    private static void Validate(EvaluateHealthConfirmation input)
    {
        if (input.RecoveryConfirmationCount <= 0
            || input.CurrentIssues.Any(issue => issue.ConsecutiveFailures < 0
                || issue.ConsecutiveRecoveries < 0
                || string.IsNullOrWhiteSpace(issue.IssueKey))
            || input.CurrentIssues.Select(issue => issue.IssueKey).Distinct(StringComparer.Ordinal).Count()
                != input.CurrentIssues.Count
            || input.ObservedIssues.Any(issue => string.IsNullOrWhiteSpace(issue.IssueKey)
                || issue.FailureConfirmationCount <= 0
                || !FindingSeverities.All.Contains(issue.Severity)))
        {
            throw new ArgumentException("The health confirmation input is invalid.", nameof(input));
        }
    }
}

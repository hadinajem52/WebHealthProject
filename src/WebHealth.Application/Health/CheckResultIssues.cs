using WebHealth.Application.Monitoring;

namespace WebHealth.Application.Health;

public static class CheckResultIssues
{
    public static IReadOnlyList<ObservedIssue> Observe(
        NormalizedCheckResult result,
        int monitorFailureConfirmationCount)
    {
        if (result.Outcome == HttpResultOutcomes.Healthy
            || result.Outcome == HttpResultOutcomes.Cancelled)
        {
            return [];
        }

        var observed = result.Findings
            .GroupBy(finding => finding.IssueKey, StringComparer.Ordinal)
            .Select(group => new ObservedIssue(
                group.Key,
                group.Select(finding => finding.Severity).Aggregate(FindingSeverities.Max),
                group
                    .Select(finding => PerformanceRules.SelectFailureConfirmationCount(
                        finding.RuleKey, monitorFailureConfirmationCount))
                    .Max()))
            .ToArray();

        return observed.Length > 0
            ? observed
            : [new ObservedIssue(
                HttpIssueIdentity.Create($"Http.{result.FailureCategory ?? "Unknown"}"),
                FindingSeverities.Critical,
                monitorFailureConfirmationCount)];
    }
}

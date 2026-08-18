using WebHealth.Application.Monitoring;

namespace WebHealth.Application.Health;

/// <summary>
/// Turns a normalized result into the issues the confirmation engine and incident automation
/// both work from. It lives in one place because the two must agree exactly: an issue the
/// engine counted but the automation did not recognise would advance a counter that never
/// opens an incident.
/// </summary>
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

        // A result can fail without producing a finding — an execution-terminal result, for
        // example. It still has to track as something, or the failure would be invisible to
        // confirmation, so it falls back to a key synthesized from its category.
        return observed.Length > 0
            ? observed
            : [new ObservedIssue(
                HttpIssueIdentity.Create($"Http.{result.FailureCategory ?? "Unknown"}"),
                FindingSeverities.Critical,
                monitorFailureConfirmationCount)];
    }
}

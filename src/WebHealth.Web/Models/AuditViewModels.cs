using WebHealth.Application.Auditing;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Models;

public sealed class AuditIndexViewModel
{
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? Action { get; init; }

    public string? Entity { get; init; }

    public required AuditSearchResult Result { get; init; }

    public required IReadOnlyList<AuditActor> Actors { get; init; }

    public required IReadOnlyList<string> Actions { get; init; }

    public required IReadOnlyList<string> EntityTypes { get; init; }

    public bool HasFilters =>
        FromDate is not null
        || ToDate is not null
        || ActorUserId is not null
        || !string.IsNullOrWhiteSpace(Action)
        || !string.IsNullOrWhiteSpace(Entity);
}

public static class AuditEventDisplay
{
    public static string OutcomeBadge(string? outcome) => outcome?.ToLowerInvariant() switch
    {
        "succeeded" => StatusBadges.Success,
        "forbidden" or "denied" or "failed" => StatusBadges.Danger,
        _ => StatusBadges.Neutral
    };

    public static string OutcomeName(string? outcome) =>
        string.IsNullOrWhiteSpace(outcome)
            ? "Unknown"
            : char.ToUpperInvariant(outcome[0]) + outcome[1..];

    private static readonly Dictionary<string, string> CompoundVerbSpacing = new(StringComparer.Ordinal)
    {
        ["failurerecorded"] = "failure recorded",
        ["recoverystarted"] = "recovery started",
        ["recoveryinterrupted"] = "recovery interrupted",
        ["inprogress"] = "in progress",
        ["noteadded"] = "note added",
        ["forceclosed"] = "force closed",
        ["schedulepaused"] = "schedule paused",
        ["scheduleresumed"] = "schedule resumed"
    };

    public static string ActionName(string action) =>
        string.Join(' ', action.Split('.').Select(part => CompoundVerbSpacing.GetValueOrDefault(part, part)));

    public static string DescribeValues(AuditEventSummary auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var fields = auditEvent.BeforeValues.Keys
            .Union(auditEvent.AfterValues.Keys, StringComparer.OrdinalIgnoreCase)
            .Count();

        var shape = auditEvent.BeforeValues.Count == 0
            ? "recorded"
            : auditEvent.AfterValues.Count == 0
                ? "removed"
                : "changed";

        return $"{fields} field{(fields == 1 ? null : "s")} {shape}";
    }
}

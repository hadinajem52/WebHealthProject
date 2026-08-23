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

    /// <summary>
    /// Whether anything narrowed this search. A Clear control offered against an unfiltered trail
    /// is an action with nothing to do, and reads as though a filter is applied when none is.
    /// </summary>
    public bool HasFilters =>
        FromDate is not null
        || ToDate is not null
        || ActorUserId is not null
        || !string.IsNullOrWhiteSpace(Action)
        || !string.IsNullOrWhiteSpace(Entity);
}

/// <summary>
/// How a recorded event is described to a reader.
/// </summary>
public static class AuditEventDisplay
{
    /// <summary>
    /// The stored outcome is a lowercase word, and every one of them rendered as the same neutral
    /// tag. An audit trail is read to find the refusals, so a refusal must not look like a
    /// success at a glance.
    /// </summary>
    public static string OutcomeBadge(string? outcome) => outcome?.ToLowerInvariant() switch
    {
        "succeeded" => StatusBadges.Success,
        "forbidden" or "denied" or "failed" => StatusBadges.Danger,
        _ => StatusBadges.Neutral
    };

    /// <summary>Sentence case, because the stored value is a bare lowercase token.</summary>
    public static string OutcomeName(string? outcome) =>
        string.IsNullOrWhiteSpace(outcome)
            ? "Unknown"
            : char.ToUpperInvariant(outcome[0]) + outcome[1..];

    /// <summary>
    /// A dotted key is how the action is stored and correlated; the spaced form is how it reads.
    /// Both are shown, so neither the reader nor a support request has to translate.
    /// </summary>
    public static string ActionName(string action) => action.Replace('.', ' ');

    /// <summary>
    /// What opening the disclosure will show, said before it is opened. "View values" gave no
    /// reason to open one row rather than another.
    /// </summary>
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

using WebHealth.Application.Incidents;
using WebHealth.Domain.Incidents;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Models;

public static class IncidentStatusDisplay
{
    public static string Name(string status) => status switch
    {
        "InProgress" => "In progress",
        "MonitoringRecovery" => "Monitoring recovery",
        _ => status
    };
}

public static class IncidentEventDisplay
{
    public static string Name(IncidentTimelineEntry entry) => IsAutomatedSeverityChange(entry)
        ? "Severity escalated"
        : entry.EventType switch
        {
            IncidentEventTypes.Opened => "Incident opened",
            IncidentEventTypes.StatusChanged => "Status changed",
            IncidentEventTypes.Reassigned => "Reassigned",
            IncidentEventTypes.NoteAdded => "Note added",
            IncidentEventTypes.CertificateRenewed => "Certificate renewed",
            _ => entry.EventType
        };

    public static string Tone(IncidentTimelineEntry entry) => entry.EventType switch
    {
        IncidentEventTypes.Opened => StatusBadges.Danger,
        IncidentEventTypes.StatusChanged => StatusBadges.ForIncidentStatus(entry.ToStatus),
        IncidentEventTypes.CertificateRenewed => StatusBadges.Success,
        IncidentEventTypes.NoteAdded when IsAutomatedSeverityChange(entry) => StatusBadges.Warning,
        _ => StatusBadges.Neutral
    };

    public static string? Elapsed(TimeSpan? elapsed)
    {
        if (elapsed is not { } gap || gap < TimeSpan.Zero)
        {
            return null;
        }

        if (gap < TimeSpan.FromMinutes(1))
        {
            return "after less than a minute";
        }

        if (gap < TimeSpan.FromHours(1))
        {
            return $"after {gap.Minutes}m";
        }

        return gap < TimeSpan.FromDays(1)
            ? $"after {(int)gap.TotalHours}h {gap.Minutes}m"
            : $"after {(int)gap.TotalDays}d {gap.Hours}h";
    }

    private static bool IsAutomatedSeverityChange(IncidentTimelineEntry entry) =>
        entry.EventType == IncidentEventTypes.NoteAdded && entry.ActorDisplayName is null;
}

public static class IncidentEvidenceDisplay
{
    private const string Separator = " · ";
    private const string NoProof = "—";

    public static string Name(string evidenceRole) => evidenceRole switch
    {
        IncidentEvidenceRoles.ConfirmationThreshold => "Incident opened",
        IncidentEvidenceRoles.ConfirmedFailure => "Failure confirmed again",
        IncidentEvidenceRoles.RecoveryInterrupted => "Recovery interrupted by a new failure",
        IncidentEvidenceRoles.RecoveryStarted => "First healthy check",
        IncidentEvidenceRoles.RecoveryConfirmed => "Recovery confirmed",
        IncidentEvidenceRoles.AutomaticRecovery => "Resolved automatically",
        IncidentEvidenceRoles.ResolveManually => "Resolved by a person",
        IncidentEvidenceRoles.ForceClose => "Force-closed by an administrator",
        _ => evidenceRole
    };

    public static string Tone(string evidenceRole) => evidenceRole switch
    {
        IncidentEvidenceRoles.RecoveryStarted or IncidentEvidenceRoles.RecoveryConfirmed
            or IncidentEvidenceRoles.AutomaticRecovery or IncidentEvidenceRoles.ResolveManually =>
            StatusBadges.Success,
        IncidentEvidenceRoles.ForceClose => StatusBadges.Warning,
        _ => StatusBadges.Danger
    };

    public static string Proof(IncidentEvidenceItem evidence)
    {
        if (evidence.ActorDisplayName is { Length: > 0 } actor)
        {
            return $"Recorded by {actor}";
        }

        if (evidence.Proof is not { } proof)
        {
            return NoProof;
        }

        var facts = new List<string>();
        if (proof.ObservedValue is { Length: > 0 } observed)
        {
            facts.Add(proof.ExpectedValue is { Length: > 0 } expected
                ? $"{observed}{Separator}expected {expected}"
                : observed);
        }
        else if (proof.SafeDiagnostic is { Length: > 0 } diagnostic)
        {
            facts.Add(diagnostic);
        }

        if (evidence.EvidenceRole == IncidentEvidenceRoles.ConfirmationThreshold
            && proof.FailureConfirmationCount is { } threshold)
        {
            facts.Add($"confirmed by {threshold} consecutive failures");
        }

        return facts.Count == 0 ? NoProof : string.Join(Separator, facts);
    }
}

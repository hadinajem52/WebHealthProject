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
    public static string Name(string eventType) => eventType switch
    {
        "StatusChanged" => "Status changed",
        "NoteAdded" => "Note added",
        "EvidenceRecorded" => "Evidence recorded",
        "CertificateRenewed" => "Certificate renewed",
        _ => eventType
    };
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

    public static string Outcome(int? httpStatus, string? failureCategory) => failureCategory is { } category
        ? IssueDisplay.DescribeFailureCategory(category)
        : httpStatus is { } status
            ? $"HTTP {status}"
            : "No response";

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

        if (proof.HttpStatus is { } httpStatus)
        {
            facts.Add($"HTTP {httpStatus}");
        }

        if (proof.TotalDurationMs is { } duration)
        {
            facts.Add($"{duration} ms");
        }

        return facts.Count == 0 ? NoProof : string.Join(Separator, facts);
    }
}

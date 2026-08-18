namespace WebHealth.Domain.Incidents;

/// <summary>
/// The severity vocabulary for anything the system reports: findings, incidents and
/// notifications all use these three values, so a rule can never raise a severity that an
/// incident cannot carry.
/// </summary>
public static class IncidentSeverities
{
    public const string Warning = "Warning";

    /// <summary>
    /// Between warning and critical. It exists for rules whose urgency escalates while the
    /// endpoint itself is still serving traffic — certificate expiry inside 15 days (BR-C04) is
    /// the first of them.
    /// </summary>
    public const string High = "High";

    public const string Critical = "Critical";

    public static readonly string[] All = [Warning, High, Critical];

    public static int Rank(string severity) => severity switch
    {
        Critical => 3,
        High => 2,
        Warning => 1,
        _ => 0
    };

    /// <summary>Returns whichever of the two severities is more urgent.</summary>
    public static string Max(string first, string second) =>
        Rank(second) > Rank(first) ? second : first;
}

public static class IncidentStatuses
{
    public const string Open = "Open";
    public const string Acknowledged = "Acknowledged";
    public const string InProgress = "InProgress";
    public const string MonitoringRecovery = "MonitoringRecovery";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";

    private static readonly string[] ActiveValues = [Open, Acknowledged, InProgress, MonitoringRecovery];

    public static IReadOnlyList<string> Active => ActiveValues;
}

public static class IncidentEventTypes
{
    public const string Opened = "Opened";
    public const string StatusChanged = "StatusChanged";
    public const string Reassigned = "Reassigned";
    public const string NoteAdded = "NoteAdded";
    public const string EvidenceRecorded = "EvidenceRecorded";

    /// <summary>
    /// A certificate with a different fingerprint replaced the one this incident was raised
    /// against (BR-C06). It is its own event type rather than a note, because the timeline has
    /// to distinguish "someone wrote something" from "the monitored subject was replaced".
    /// </summary>
    public const string CertificateRenewed = "CertificateRenewed";
}

public static class IncidentEvidenceTypes
{
    public const string Opening = "Opening";
    public const string Failure = "Failure";
    public const string Recovery = "Recovery";
    public const string Resolution = "Resolution";
}

public static class IncidentResolutionCategories
{
    public const string AutomaticRecovery = "AutomaticRecovery";
    public const string ForcedClosure = "ForcedClosure";

    /// <summary>
    /// The certificate the incident tracked was renewed, so the incident's subject no longer
    /// exists to recover (BR-C06). Distinct from <see cref="AutomaticRecovery" />, which means
    /// the same subject started passing again.
    /// </summary>
    public const string CertificateRenewed = "CertificateRenewed";
}

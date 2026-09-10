namespace WebHealth.Domain.Incidents;

public static class IncidentSeverities
{
    public const string Warning = "Warning";

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

    public const string CertificateRenewed = "CertificateRenewed";
}

public static class IncidentEvidenceTypes
{
    public const string Opening = "Opening";
    public const string Failure = "Failure";
    public const string Recovery = "Recovery";
    public const string Resolution = "Resolution";
}

public static class IncidentEvidenceRoles
{
    public const string ConfirmationThreshold = "ConfirmationThreshold";
    public const string ConfirmedFailure = "ConfirmedFailure";
    public const string RecoveryInterrupted = "RecoveryInterrupted";
    public const string RecoveryStarted = "RecoveryStarted";
    public const string RecoveryConfirmed = "RecoveryConfirmed";
    public const string AutomaticRecovery = "AutomaticRecovery";
    public const string ResolveManually = "ResolveManually";
    public const string ForceClose = "ForceClose";
}

public static class IncidentResolutionCategories
{
    public const string AutomaticRecovery = "AutomaticRecovery";
    public const string ForcedClosure = "ForcedClosure";

    public const string CertificateRenewed = "CertificateRenewed";
}

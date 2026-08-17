namespace WebHealth.Domain.Incidents;

public static class IncidentSeverities
{
    public const string Warning = "Warning";
    public const string Critical = "Critical";
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
}

public static class IncidentEvidenceTypes
{
    public const string Opening = "Opening";
    public const string Failure = "Failure";
    public const string Recovery = "Recovery";
}

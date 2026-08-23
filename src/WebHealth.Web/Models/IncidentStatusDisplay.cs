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

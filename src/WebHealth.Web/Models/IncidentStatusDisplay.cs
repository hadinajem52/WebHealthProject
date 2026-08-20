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

using WebHealth.Application.Monitoring;

namespace WebHealth.Web.Models;

public static class MonitorSourceDisplay
{
    public static string Name(string? source) => source switch
    {
        HttpResultNormalizer.MonitorSource => "WebHealth HTTP monitor",
        SslResultNormalizer.MonitorSource => "WebHealth certificate monitor",
        null or "" => "—",
        _ => source
    };
}

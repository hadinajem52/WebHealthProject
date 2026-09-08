using WebHealth.Domain.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal static class CheckCurrentState
{
    public static string Disposition(LogicalCheck check)
    {
        var monitor = check.EndpointMonitor;
        var snapshot = check.ConfigurationSnapshot;
        if (monitor.DeletedAt is not null
            || snapshot.ConfigurationFingerprint != monitor.ConfigurationFingerprint
            || (snapshot.SchemaVersion == 2 && snapshot.CurrentTruthGeneration != monitor.CurrentTruthGeneration))
        {
            return "Superseded";
        }
        var endpoint = monitor.Endpoint;
        var environment = endpoint.Environment;
        var website = environment.Website;
        var client = website.Client;
        if (endpoint.DeletedAt is not null || !endpoint.IsEnabled
            || environment.DeletedAt is not null || !environment.IsActive
            || website.DeletedAt is not null || !website.IsEnabled
            || client.DeletedAt is not null || !client.IsActive
            || (check.Source == LogicalCheckSources.Scheduled && (!monitor.IsEnabled || !monitor.SchedulingEnabled)))
        {
            return "Ineligible";
        }
        return "Current";
    }
}

using Hangfire;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class HangfireMonitoringWorkerReader(JobStorage storage, ILogger<HangfireMonitoringWorkerReader> logger)
    : IMonitoringWorkerReader
{
    public MonitoringWorkerStatus Read()
    {
        try
        {
            var servers = storage.GetMonitoringApi().Servers();
            var covered = servers.Where(server => server.WorkersCount > 0
                && server.Queues.Contains(MonitoringQueueNames.ShortChecks)).ToArray();
            var heartbeat = covered.Where(server => server.Heartbeat.HasValue)
                .Select(server => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(server.Heartbeat!.Value, DateTimeKind.Utc)))
                .Max();
            return new(true, servers.Count > 0, covered.Length > 0, heartbeat);
        }
        catch (Exception)
        {
            logger.LogWarning("Monitoring worker status could not be read");
            return new(false, false, false, null);
        }
    }
}

internal sealed class DisabledMonitoringWorkerReader : IMonitoringWorkerReader
{
    public MonitoringWorkerStatus Read() => new(false, false, false, null);
}

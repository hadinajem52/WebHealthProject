using Hangfire;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class MonitoringDispatchJob(IMonitoringSchedulingService schedulingService)
{
    [Queue(MonitoringQueueNames.ShortChecks)]
    [AutomaticRetry(Attempts = 0)]
    public async Task DispatchAsync(CancellationToken cancellationToken) =>
        await schedulingService.DispatchDueAsync(cancellationToken);

    [Queue(MonitoringQueueNames.ShortChecks)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ReconcileAsync(CancellationToken cancellationToken) =>
        await schedulingService.ReconcileAsync(cancellationToken);
}

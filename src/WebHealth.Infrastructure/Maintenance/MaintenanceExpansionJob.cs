using Hangfire;
using WebHealth.Application.Maintenance;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceExpansionJob(IMaintenanceOccurrenceExpander expander)
{
    [Queue(MaintenanceQueueNames.Maintenance)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExpandAsync(CancellationToken cancellationToken) =>
        await expander.ExpandDueAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Maintenance;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceEvaluator(ApplicationDbContext dbContext) : IMaintenanceEvaluator
{
    public async Task<ActiveMaintenanceOccurrence?> FindActiveAsync(Guid endpointMonitorId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var monitor = await dbContext.EndpointMonitors.AsNoTracking().Where(item => item.Id == endpointMonitorId)
            .Select(item => new { item.Id, item.EndpointId, item.Endpoint.EnvironmentId, item.Endpoint.Environment.WebsiteId, item.Endpoint.Environment.Website.ClientId })
            .SingleOrDefaultAsync(cancellationToken);
        if (monitor is null) return null;

        return await dbContext.MaintenanceOccurrences.AsNoTracking()
            .Where(item => item.StartsAt <= at && item.EndsAt > at && item.MaintenanceWindow.DeletedAt == null
                && item.MaintenanceWindow.Targets.Any(target => target.EndpointMonitorId == monitor.Id
                    || target.EndpointId == monitor.EndpointId || target.EnvironmentId == monitor.EnvironmentId
                    || target.WebsiteId == monitor.WebsiteId || target.ClientId == monitor.ClientId))
            .OrderBy(item => item.StartsAt)
            .Select(item => new ActiveMaintenanceOccurrence(item.Id, item.MaintenanceWindow.SuppressionPolicy,
                item.MaintenanceWindow.PauseEscalation, item.MaintenanceWindow.ContinueFailureCounter))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

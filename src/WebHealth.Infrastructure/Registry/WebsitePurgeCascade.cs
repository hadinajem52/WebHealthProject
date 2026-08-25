using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class WebsitePurgeCascade(
    ApplicationDbContext dbContext,
    EndpointPurgeCascade endpointPurge)
{
    public async Task ExecuteAsync(Guid websiteId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "SET LOCAL web_health.endpoint_purge = 'on'", cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM web_health.website WHERE id = {websiteId} FOR UPDATE
            """, cancellationToken);

        var environmentIds = await dbContext.Environments.AsNoTracking()
            .Where(environment => environment.WebsiteId == websiteId)
            .OrderBy(environment => environment.Id)
            .Select(environment => environment.Id)
            .ToArrayAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM web_health.environment
            WHERE website_id = {websiteId}
            ORDER BY id FOR UPDATE
            """, cancellationToken);

        var endpointIds = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => environmentIds.Contains(endpoint.EnvironmentId))
            .OrderBy(endpoint => endpoint.Id)
            .Select(endpoint => endpoint.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var endpointId in endpointIds)
        {
            await endpointPurge.ExecuteAsync(endpointId, cancellationToken);
        }

        var maintenanceWindowIds = await dbContext.MaintenanceTargets.AsNoTracking()
            .Where(target => target.WebsiteId == websiteId
                || (target.EnvironmentId != null && environmentIds.Contains(target.EnvironmentId.Value)))
            .Select(target => target.MaintenanceWindowId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (maintenanceWindowIds.Length > 0)
        {
            await dbContext.MaintenanceTargets
                .Where(target => target.WebsiteId == websiteId
                    || (target.EnvironmentId != null && environmentIds.Contains(target.EnvironmentId.Value)))
                .ExecuteDeleteAsync(cancellationToken);
            var orphanedWindowIds = await dbContext.MaintenanceWindows.AsNoTracking()
                .Where(window => maintenanceWindowIds.Contains(window.Id)
                    && !dbContext.MaintenanceTargets.Any(target => target.MaintenanceWindowId == window.Id))
                .Select(window => window.Id)
                .ToArrayAsync(cancellationToken);
            await dbContext.MaintenanceOccurrences
                .Where(occurrence => orphanedWindowIds.Contains(occurrence.MaintenanceWindowId))
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.MaintenanceWindows
                .Where(window => orphanedWindowIds.Contains(window.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await dbContext.AccessGrants
            .Where(grant => grant.WebsiteId == websiteId
                || (grant.EnvironmentId != null && environmentIds.Contains(grant.EnvironmentId.Value)))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Environments
            .Where(environment => environment.WebsiteId == websiteId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.WebsiteTags
            .Where(websiteTag => websiteTag.WebsiteId == websiteId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Websites
            .Where(website => website.Id == websiteId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

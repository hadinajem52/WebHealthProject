using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

/// <summary>
/// Removes a website, every environment under it, and every endpoint under those.
/// </summary>
/// <remarks>
/// The endpoint layer is delegated to <see cref="EndpointPurgeCascade" /> rather than restated
/// here. An endpoint reaches twenty-odd tables and one of them - the origin's robots policy - is
/// only removable conditionally; a second copy of that ordering would be a second thing to keep
/// correct, and the first table either copy forgot would abort the purge under RESTRICT.
/// <para>
/// What is left for this class is the part an endpoint purge cannot see: the rows that hang off
/// the environment and the website themselves.
/// </para>
/// </remarks>
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

        // Read in full before anything is deleted: each purge below removes the endpoint row that
        // this query selects over, so a lazily re-evaluated version would shrink as it ran.
        var endpointIds = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => environmentIds.Contains(endpoint.EnvironmentId))
            .OrderBy(endpoint => endpoint.Id)
            .Select(endpoint => endpoint.Id)
            .ToArrayAsync(cancellationToken);

        // Sequentially, and deliberately so: two endpoints on one host both ask whether they are
        // the last on their origin, and that question only has a stable answer if one of them has
        // already finished.
        foreach (var endpointId in endpointIds)
        {
            await endpointPurge.ExecuteAsync(endpointId, cancellationToken);
        }

        // A maintenance window scoped to an environment of this website targets nothing else, so
        // it goes with the website rather than being left with no target to describe. Read before
        // the target rows are deleted, for the same reason the endpoint cascade does.
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

        // The join row only, never the tag: a tag is shared vocabulary across websites, and
        // removing one because a single website used it would edit every other website's meaning.
        await dbContext.WebsiteTags
            .Where(websiteTag => websiteTag.WebsiteId == websiteId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Websites
            .Where(website => website.Id == websiteId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

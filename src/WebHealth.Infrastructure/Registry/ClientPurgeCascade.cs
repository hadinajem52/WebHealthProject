using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class ClientPurgeCascade(
    ApplicationDbContext dbContext,
    WebsitePurgeCascade websitePurge)
{
    public async Task ExecuteAsync(Guid clientId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "SET LOCAL web_health.endpoint_purge = 'on'", cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM web_health.client WHERE id = {clientId} FOR UPDATE
            """, cancellationToken);

        var websiteIds = await dbContext.Websites.AsNoTracking()
            .Where(website => website.ClientId == clientId)
            .OrderBy(website => website.Id)
            .Select(website => website.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var websiteId in websiteIds)
        {
            await websitePurge.ExecuteAsync(websiteId, cancellationToken);
        }

        await MaintenanceScopePurge.RemoveTargetsAsync(
            dbContext,
            target => target.ClientId == clientId,
            cancellationToken);

        await dbContext.AccessGrants
            .Where(grant => grant.ClientId == clientId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Clients
            .Where(client => client.Id == clientId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

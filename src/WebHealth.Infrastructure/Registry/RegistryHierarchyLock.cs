using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class RegistryHierarchyLock(ApplicationDbContext dbContext)
{
    internal Task<Client?> LockClientAsync(Guid clientId, CancellationToken cancellationToken) =>
        dbContext.Clients.FromSqlInterpolated($"""
            SELECT * FROM web_health.client WHERE id = {clientId} FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    internal Task<Website?> LockWebsiteAsync(Guid websiteId, CancellationToken cancellationToken) =>
        dbContext.Websites.FromSqlInterpolated($"""
            SELECT * FROM web_health.website WHERE id = {websiteId} FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    internal Task<WebsiteEnvironment?> LockEnvironmentAsync(
        Guid environmentId,
        CancellationToken cancellationToken) =>
        dbContext.Environments.FromSqlInterpolated($"""
            SELECT * FROM web_health.environment WHERE id = {environmentId} FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    internal async Task<LockedWebsiteHierarchy?> LockWebsiteHierarchyAsync(
        Guid websiteId,
        CancellationToken cancellationToken)
    {
        var website = await LockWebsiteAsync(websiteId, cancellationToken);
        if (website is null)
        {
            return null;
        }

        var client = await LockClientAsync(website.ClientId, cancellationToken);
        return client is null ? null : new(website, client);
    }

    internal async Task<LockedEnvironmentHierarchy?> LockEnvironmentHierarchyAsync(
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        var environment = await LockEnvironmentAsync(environmentId, cancellationToken);
        if (environment is null)
        {
            return null;
        }

        var website = await LockWebsiteHierarchyAsync(environment.WebsiteId, cancellationToken);
        return website is null ? null : new(environment, website.Website, website.Client);
    }
}

internal sealed record LockedWebsiteHierarchy(Website Website, Client Client);

internal sealed record LockedEnvironmentHierarchy(
    WebsiteEnvironment Environment,
    Website Website,
    Client Client);

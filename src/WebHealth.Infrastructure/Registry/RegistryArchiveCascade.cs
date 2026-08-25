using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class RegistryArchiveCascade(ApplicationDbContext dbContext)
{
    public async Task ArchiveClientDescendantsAsync(
        Guid clientId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await dbContext.Websites
            .Where(website => website.ClientId == clientId && website.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(website => website.IsEnabled, false)
                .SetProperty(website => website.DeletedAt, now)
                .SetProperty(website => website.DeletedByUserId, actorId)
                .SetProperty(website => website.UpdatedAt, now)
                .SetProperty(website => website.UpdatedByUserId, actorId)
                .SetProperty(website => website.Version, website => website.Version + 1),
                cancellationToken);

        await ArchiveEnvironmentsAsync(
            dbContext.Environments.Where(environment => environment.Website.ClientId == clientId),
            actorId,
            now,
            cancellationToken);

        await ArchiveEndpointsAsync(
            dbContext.Endpoints.Where(endpoint => endpoint.Environment.Website.ClientId == clientId),
            dbContext.EndpointMonitors.Where(monitor =>
                monitor.Endpoint.Environment.Website.ClientId == clientId),
            actorId,
            now,
            cancellationToken);
    }

    public async Task ArchiveWebsiteDescendantsAsync(
        Guid websiteId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ArchiveEnvironmentsAsync(
            dbContext.Environments.Where(environment => environment.WebsiteId == websiteId),
            actorId,
            now,
            cancellationToken);

        await ArchiveEndpointsAsync(
            dbContext.Endpoints.Where(endpoint => endpoint.Environment.WebsiteId == websiteId),
            dbContext.EndpointMonitors.Where(monitor => monitor.Endpoint.Environment.WebsiteId == websiteId),
            actorId,
            now,
            cancellationToken);
    }

    public Task ArchiveEnvironmentDescendantsAsync(
        Guid environmentId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ArchiveEndpointsAsync(
            dbContext.Endpoints.Where(endpoint => endpoint.EnvironmentId == environmentId),
            dbContext.EndpointMonitors.Where(monitor => monitor.Endpoint.EnvironmentId == environmentId),
            actorId,
            now,
            cancellationToken);

    private static Task ArchiveEnvironmentsAsync(
        IQueryable<WebsiteEnvironment> environments,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        environments
            .Where(environment => environment.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(environment => environment.IsActive, false)
                .SetProperty(environment => environment.DeletedAt, now)
                .SetProperty(environment => environment.DeletedByUserId, actorId)
                .SetProperty(environment => environment.UpdatedAt, now)
                .SetProperty(environment => environment.UpdatedByUserId, actorId)
                .SetProperty(environment => environment.Version, environment => environment.Version + 1),
                cancellationToken);

    private static async Task ArchiveEndpointsAsync(
        IQueryable<Endpoint> endpoints,
        IQueryable<EndpointMonitor> monitors,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await monitors
            .Where(monitor => monitor.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(monitor => monitor.DeletedAt, now)
                .SetProperty(monitor => monitor.DeletedByUserId, actorId)
                .SetProperty(monitor => monitor.UpdatedAt, now)
                .SetProperty(monitor => monitor.UpdatedByUserId, actorId)
                .SetProperty(monitor => monitor.Version, monitor => monitor.Version + 1),
                cancellationToken);

        await endpoints
            .Where(endpoint => endpoint.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(endpoint => endpoint.IsEnabled, false)
                .SetProperty(endpoint => endpoint.DeletedAt, now)
                .SetProperty(endpoint => endpoint.DeletedByUserId, actorId)
                .SetProperty(endpoint => endpoint.UpdatedAt, now)
                .SetProperty(endpoint => endpoint.UpdatedByUserId, actorId)
                .SetProperty(endpoint => endpoint.Version, endpoint => endpoint.Version + 1),
                cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class RegistryVisibility(ApplicationDbContext dbContext)
{
    public IQueryable<Client> ApplyClientScope(
        IQueryable<Client> clients,
        RegistryAccessContext access,
        DateTimeOffset now)
    {
        if (HasGlobalAccess(access))
        {
            return clients;
        }

        var isDeveloper = access.Roles.Contains(ApplicationRoles.DeveloperSupport);
        var isViewer = access.Roles.Contains(ApplicationRoles.Viewer);
        var assignedOwnerIds = AssignedOwnerIds(access.UserId, now);
        var grantedClientIds = ActiveGrants(access.UserId, now)
            .Where(grant => grant.ClientId != null)
            .Select(grant => grant.ClientId!.Value);
        var grantedWebsiteIds = ActiveGrants(access.UserId, now)
            .Where(grant => grant.WebsiteId != null)
            .Select(grant => grant.WebsiteId!.Value);
        var grantedEnvironmentWebsiteIds = ActiveGrants(access.UserId, now)
            .Where(grant => grant.EnvironmentId != null)
            .Join(
                dbContext.Environments,
                grant => grant.EnvironmentId,
                environment => environment.Id,
                (_, environment) => environment.WebsiteId);
        var grantedEndpointWebsiteIds = ActiveGrants(access.UserId, now)
            .Where(grant => grant.EndpointId != null)
            .Join(dbContext.Endpoints, grant => grant.EndpointId, endpoint => endpoint.Id, (_, endpoint) => endpoint)
            .Join(dbContext.Environments, endpoint => endpoint.EnvironmentId, environment => environment.Id, (_, environment) => environment.WebsiteId);

        return clients.Where(client =>
            (isDeveloper && (assignedOwnerIds.Contains(client.OwnerSubjectId)
                || client.Websites.Any(website => assignedOwnerIds.Contains(website.OwnerSubjectId)
                    || website.Environments.Any(environment => environment.Endpoints.Any(endpoint =>
                        endpoint.OwnerSubjectId != null && assignedOwnerIds.Contains(endpoint.OwnerSubjectId.Value))))))
            || (isViewer && (grantedClientIds.Contains(client.Id)
                || client.Websites.Any(website =>
                    grantedWebsiteIds.Contains(website.Id)
                    || grantedEnvironmentWebsiteIds.Contains(website.Id)
                    || grantedEndpointWebsiteIds.Contains(website.Id)))));
    }

    public IQueryable<Website> ApplyWebsiteScope(
        IQueryable<Website> websites,
        RegistryAccessContext access,
        DateTimeOffset now)
    {
        if (HasGlobalAccess(access))
        {
            return websites;
        }

        var isDeveloper = access.Roles.Contains(ApplicationRoles.DeveloperSupport);
        var isViewer = access.Roles.Contains(ApplicationRoles.Viewer);
        var assignedOwnerIds = AssignedOwnerIds(access.UserId, now);
        var activeGrants = ActiveGrants(access.UserId, now);
        var grantedClientIds = activeGrants.Where(grant => grant.ClientId != null)
            .Select(grant => grant.ClientId!.Value);
        var grantedWebsiteIds = activeGrants.Where(grant => grant.WebsiteId != null)
            .Select(grant => grant.WebsiteId!.Value);
        var grantedEnvironmentWebsiteIds = activeGrants.Where(grant => grant.EnvironmentId != null)
            .Join(
                dbContext.Environments,
                grant => grant.EnvironmentId,
                environment => environment.Id,
                (_, environment) => environment.WebsiteId);
        var grantedEndpointIds = activeGrants.Where(grant => grant.EndpointId != null)
            .Select(grant => grant.EndpointId!.Value);

        return websites.Where(website =>
            (isDeveloper && (assignedOwnerIds.Contains(website.Client.OwnerSubjectId)
                || assignedOwnerIds.Contains(website.OwnerSubjectId)
                || website.Environments.Any(environment => environment.Endpoints.Any(endpoint =>
                    endpoint.OwnerSubjectId != null && assignedOwnerIds.Contains(endpoint.OwnerSubjectId.Value)))))
            || (isViewer && (grantedClientIds.Contains(website.ClientId)
                || grantedWebsiteIds.Contains(website.Id)
                || grantedEnvironmentWebsiteIds.Contains(website.Id)
                || website.Environments.Any(environment => environment.Endpoints.Any(endpoint =>
                    grantedEndpointIds.Contains(endpoint.Id))))));
    }

    public IQueryable<WebsiteEnvironment> ApplyEnvironmentScope(
        IQueryable<WebsiteEnvironment> environments,
        RegistryAccessContext access,
        DateTimeOffset now)
    {
        if (HasGlobalAccess(access))
        {
            return environments;
        }

        var isDeveloper = access.Roles.Contains(ApplicationRoles.DeveloperSupport);
        var isViewer = access.Roles.Contains(ApplicationRoles.Viewer);
        var assignedOwnerIds = AssignedOwnerIds(access.UserId, now);
        var grants = ActiveGrants(access.UserId, now);
        var clientIds = grants.Where(grant => grant.ClientId != null).Select(grant => grant.ClientId!.Value);
        var websiteIds = grants.Where(grant => grant.WebsiteId != null).Select(grant => grant.WebsiteId!.Value);
        var environmentIds = grants.Where(grant => grant.EnvironmentId != null).Select(grant => grant.EnvironmentId!.Value);
        var endpointIds = grants.Where(grant => grant.EndpointId != null).Select(grant => grant.EndpointId!.Value);

        return environments.Where(environment =>
            (isDeveloper && (assignedOwnerIds.Contains(environment.Website.Client.OwnerSubjectId)
                || assignedOwnerIds.Contains(environment.Website.OwnerSubjectId)
                || environment.Endpoints.Any(endpoint => endpoint.OwnerSubjectId != null
                    && assignedOwnerIds.Contains(endpoint.OwnerSubjectId.Value))))
            || (isViewer && (clientIds.Contains(environment.Website.ClientId)
                || websiteIds.Contains(environment.WebsiteId)
                || environmentIds.Contains(environment.Id)
                || environment.Endpoints.Any(endpoint => endpointIds.Contains(endpoint.Id)))));
    }

    public IQueryable<Endpoint> ApplyEndpointScope(
        IQueryable<Endpoint> endpoints,
        RegistryAccessContext access,
        DateTimeOffset now)
    {
        if (HasGlobalAccess(access))
        {
            return endpoints;
        }

        var isDeveloper = access.Roles.Contains(ApplicationRoles.DeveloperSupport);
        var isViewer = access.Roles.Contains(ApplicationRoles.Viewer);
        var assignedOwnerIds = AssignedOwnerIds(access.UserId, now);
        var grants = ActiveGrants(access.UserId, now);
        var clientIds = grants.Where(grant => grant.ClientId != null).Select(grant => grant.ClientId!.Value);
        var websiteIds = grants.Where(grant => grant.WebsiteId != null).Select(grant => grant.WebsiteId!.Value);
        var environmentIds = grants.Where(grant => grant.EnvironmentId != null).Select(grant => grant.EnvironmentId!.Value);
        var endpointIds = grants.Where(grant => grant.EndpointId != null).Select(grant => grant.EndpointId!.Value);

        return endpoints.Where(endpoint =>
            (isDeveloper && assignedOwnerIds.Contains(
                endpoint.OwnerSubjectId ?? endpoint.Environment.Website.OwnerSubjectId))
            || (isViewer && (clientIds.Contains(endpoint.Environment.Website.ClientId)
                || websiteIds.Contains(endpoint.Environment.WebsiteId)
                || environmentIds.Contains(endpoint.EnvironmentId)
                || endpointIds.Contains(endpoint.Id))));
    }

    public IQueryable<Endpoint> ApplyTestableEndpointScope(
        IQueryable<Endpoint> endpoints,
        RegistryAccessContext access,
        DateTimeOffset now)
    {
        if (HasGlobalAccess(access))
        {
            return endpoints;
        }

        if (!access.Roles.Contains(ApplicationRoles.DeveloperSupport))
        {
            return endpoints.Where(_ => false);
        }

        var assignedOwnerIds = AssignedOwnerIds(access.UserId, now);
        return endpoints.Where(endpoint =>
            assignedOwnerIds.Contains(endpoint.OwnerSubjectId ?? endpoint.Environment.Website.OwnerSubjectId));
    }

    public static bool CanManage(RegistryAccessContext access) =>
        access.Roles.Contains(ApplicationRoles.Administrator)
        || access.Roles.Contains(ApplicationRoles.Operations);

    private static bool HasGlobalAccess(RegistryAccessContext access) => CanManage(access);

    internal IQueryable<Guid> AssignedOwnerIds(Guid userId, DateTimeOffset now) =>
        dbContext.OwnerSubjects.Where(subject =>
                dbContext.Users.Any(user => user.Id == userId && !user.IsDisabled)
                && (subject.UserId == userId
                    || dbContext.TeamMembers.Any(member =>
                        member.TeamId == subject.TeamId
                        && member.UserId == userId
                        && !member.Team.IsDisabled
                        && !member.User.IsDisabled
                        && member.EffectiveFrom <= now
                        && (member.EffectiveUntil == null || member.EffectiveUntil > now))))
            .Select(subject => subject.Id);

    private IQueryable<AccessGrant> ActiveGrants(Guid userId, DateTimeOffset now) =>
        dbContext.AccessGrants.Where(grant =>
            grant.UserId == userId
            && dbContext.Users.Any(user => user.Id == grant.UserId && !user.IsDisabled)
            && grant.EffectiveFrom <= now
            && grant.RevokedAt == null
            && (grant.ExpiresAt == null || grant.ExpiresAt > now));
}

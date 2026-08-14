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

        return clients.Where(client =>
            (isDeveloper && (assignedOwnerIds.Contains(client.OwnerSubjectId)
                || client.Websites.Any(website => assignedOwnerIds.Contains(website.OwnerSubjectId))))
            || (isViewer && (grantedClientIds.Contains(client.Id)
                || client.Websites.Any(website =>
                    grantedWebsiteIds.Contains(website.Id)
                    || grantedEnvironmentWebsiteIds.Contains(website.Id)))));
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

        return websites.Where(website =>
            (isDeveloper && (assignedOwnerIds.Contains(website.Client.OwnerSubjectId)
                || assignedOwnerIds.Contains(website.OwnerSubjectId)))
            || (isViewer && (grantedClientIds.Contains(website.ClientId)
                || grantedWebsiteIds.Contains(website.Id)
                || grantedEnvironmentWebsiteIds.Contains(website.Id))));
    }

    public static bool CanManage(RegistryAccessContext access) =>
        access.Roles.Contains(ApplicationRoles.Administrator)
        || access.Roles.Contains(ApplicationRoles.Operations);

    private static bool HasGlobalAccess(RegistryAccessContext access) => CanManage(access);

    private IQueryable<Guid> AssignedOwnerIds(Guid userId, DateTimeOffset now) =>
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

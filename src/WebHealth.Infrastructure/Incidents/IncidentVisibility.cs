using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Incidents;

internal sealed class IncidentVisibility(
    ApplicationDbContext dbContext,
    RegistryVisibility registryVisibility)
{
    public IQueryable<Incident> Apply(
        IQueryable<Incident> incidents,
        RegistryAccessContext access,
        DateTimeOffset now)
    {
        if (RegistryVisibility.CanManage(access))
        {
            return incidents;
        }

        var assignedOwnerIds = registryVisibility.AssignedOwnerIds(access.UserId, now);
        var visibleEndpointIds = registryVisibility
            .ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Select(endpoint => endpoint.Id);

        return incidents.Where(incident =>
            (access.Roles.Contains(ApplicationRoles.DeveloperSupport)
                && assignedOwnerIds.Contains(incident.OwnerSubjectId))
            || (access.Roles.Contains(ApplicationRoles.Viewer)
                && visibleEndpointIds.Contains(incident.EndpointMonitor.EndpointId)));
    }
}

using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class TargetAuthorizationService(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility) : ITargetAuthorizationService
{
    public Task<bool> CanTestEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return MonitoringEligibility.Apply(
                visibility.ApplyTestableEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now),
                now)
            .AnyAsync(endpoint => endpoint.Id == endpointId && endpoint.DeletedAt == null, cancellationToken);
    }
}

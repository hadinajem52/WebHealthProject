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
        return MonitoringEligibility.ApplyTestable(
                visibility.ApplyTestableEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now),
                now)
            .AnyAsync(endpoint => endpoint.Id == endpointId && endpoint.DeletedAt == null, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> FilterTestableEndpointsAsync(
        IReadOnlyCollection<Guid> endpointIds,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (endpointIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var now = DateTimeOffset.UtcNow;
        var testable = await MonitoringEligibility.ApplyTestable(
                visibility.ApplyTestableEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now),
                now)
            .Where(endpoint => endpointIds.Contains(endpoint.Id) && endpoint.DeletedAt == null)
            .Select(endpoint => endpoint.Id)
            .ToListAsync(cancellationToken);
        return testable.ToHashSet();
    }
}

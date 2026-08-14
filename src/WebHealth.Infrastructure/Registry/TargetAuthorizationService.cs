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
        CancellationToken cancellationToken = default) =>
        visibility.ApplyTestableEndpointScope(
                dbContext.Endpoints.AsNoTracking(),
                access,
                DateTimeOffset.UtcNow)
            .AnyAsync(endpoint => endpoint.Id == endpointId && endpoint.DeletedAt == null, cancellationToken);
}

using System.Net;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SystemMonitoringDnsResolver : IMonitoringDnsResolver
{
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken = default) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}

internal sealed class StrictDestinationAddressPolicy : IDestinationAddressPolicy
{
    public bool IsAllowed(IPAddress address) => DestinationAddressPolicy.IsAllowed(address);
}

internal sealed class MonitoringTargetAuthorizer(ApplicationDbContext dbContext) : IMonitoringTargetAuthorizer
{
    public Task<bool> IsAuthorizedAsync(
        Guid endpointId,
        string normalizedHost,
        int port,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        dbContext.TargetAuthorizations.AsNoTracking().AnyAsync(evidence =>
            evidence.EndpointId == endpointId
            && evidence.NormalizedHost == normalizedHost
            && evidence.Port == port
            && evidence.RevokedAt == null
            && evidence.EffectiveFrom <= at
            && (evidence.ExpiresAt == null || evidence.ExpiresAt > at),
            cancellationToken);
}

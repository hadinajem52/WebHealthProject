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

/// <summary>
/// Evidence that this endpoint may be reached at this host and port.
/// <para>
/// Its context comes from the factory and lives for the length of one query. Every outbound
/// request passes through here -- including each redirect hop -- and the crawler makes several at
/// once, so on the request's shared context two concurrent fetches meant two operations on one
/// <c>DbContext</c>, which is the exception that ended a crawl mid-run. The query reads one row
/// and tracks nothing, so a context of its own costs a pooled connection and no correctness.
/// </para>
/// </summary>
internal sealed class MonitoringTargetAuthorizer(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IMonitoringTargetAuthorizer
{
    public async Task<bool> IsAuthorizedAsync(
        Guid endpointId,
        string normalizedHost,
        int port,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.TargetAuthorizations.AsNoTracking().AnyAsync(evidence =>
            evidence.EndpointId == endpointId
            && evidence.NormalizedHost == normalizedHost
            && evidence.Port == port
            && evidence.RevokedAt == null
            && evidence.EffectiveFrom <= at
            && (evidence.ExpiresAt == null || evidence.ExpiresAt > at),
            cancellationToken);
    }
}

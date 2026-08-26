using System.Net;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;

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

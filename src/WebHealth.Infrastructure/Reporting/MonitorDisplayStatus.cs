using System.Linq.Expressions;
using WebHealth.Domain.Health;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Reporting;

public static class MonitorDisplayStatus
{
    public static Expression<Func<EndpointMonitor, string>> Projection => monitor =>
        monitor.EndpointHealth == null || monitor.EndpointHealth.ConfirmedStatus == EndpointHealthStatuses.Disabled
            ? EndpointHealthStatuses.Unknown : monitor.EndpointHealth.ConfirmedStatus;

    public static Expression<Func<EndpointMonitor, bool>> Matches(string status) => status switch
    {
        EndpointHealthStatuses.Disabled => monitor => false,
        EndpointHealthStatuses.Unknown => monitor => monitor.EndpointHealth == null
            || monitor.EndpointHealth.ConfirmedStatus == EndpointHealthStatuses.Unknown
            || monitor.EndpointHealth.ConfirmedStatus == EndpointHealthStatuses.Disabled,
        _ => monitor => monitor.EndpointHealth != null && monitor.EndpointHealth.ConfirmedStatus == status
    };
}

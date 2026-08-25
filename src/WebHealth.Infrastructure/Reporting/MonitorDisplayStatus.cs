using System.Linq.Expressions;
using WebHealth.Domain.Health;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Reporting;

public static class MonitorDisplayStatus
{
    public static bool IsLive(bool monitorEnabled, bool endpointEnabled) =>
        monitorEnabled && endpointEnabled;

    public static string Of(bool monitorEnabled, bool endpointEnabled, string? confirmedStatus) =>
        !IsLive(monitorEnabled, endpointEnabled) ? EndpointHealthStatuses.Disabled
        : confirmedStatus ?? EndpointHealthStatuses.Unknown;

    public static Expression<Func<EndpointMonitor, string>> Projection => monitor =>
        !monitor.IsEnabled
            || !monitor.Endpoint.IsEnabled
            || monitor.Endpoint.DeletedAt != null
            || !monitor.Endpoint.Environment.IsActive
            || monitor.Endpoint.Environment.DeletedAt != null
            || !monitor.Endpoint.Environment.Website.IsEnabled
            || monitor.Endpoint.Environment.Website.DeletedAt != null
            || !monitor.Endpoint.Environment.Website.Client.IsActive
            || monitor.Endpoint.Environment.Website.Client.DeletedAt != null
            ? EndpointHealthStatuses.Disabled
        : monitor.EndpointHealth == null ? EndpointHealthStatuses.Unknown
        : monitor.EndpointHealth.ConfirmedStatus;

    public static Expression<Func<EndpointMonitor, bool>> Matches(string status) => status switch
    {
        EndpointHealthStatuses.Disabled => monitor =>
            !monitor.IsEnabled
            || !monitor.Endpoint.IsEnabled
            || monitor.Endpoint.DeletedAt != null
            || !monitor.Endpoint.Environment.IsActive
            || monitor.Endpoint.Environment.DeletedAt != null
            || !monitor.Endpoint.Environment.Website.IsEnabled
            || monitor.Endpoint.Environment.Website.DeletedAt != null
            || !monitor.Endpoint.Environment.Website.Client.IsActive
            || monitor.Endpoint.Environment.Website.Client.DeletedAt != null,

        EndpointHealthStatuses.Unknown => monitor => monitor.IsEnabled && monitor.Endpoint.IsEnabled
            && monitor.Endpoint.DeletedAt == null
            && monitor.Endpoint.Environment.IsActive && monitor.Endpoint.Environment.DeletedAt == null
            && monitor.Endpoint.Environment.Website.IsEnabled
            && monitor.Endpoint.Environment.Website.DeletedAt == null
            && monitor.Endpoint.Environment.Website.Client.IsActive
            && monitor.Endpoint.Environment.Website.Client.DeletedAt == null
            && (monitor.EndpointHealth == null
                || monitor.EndpointHealth.ConfirmedStatus == EndpointHealthStatuses.Unknown),

        _ => monitor => monitor.IsEnabled && monitor.Endpoint.IsEnabled
            && monitor.Endpoint.DeletedAt == null
            && monitor.Endpoint.Environment.IsActive && monitor.Endpoint.Environment.DeletedAt == null
            && monitor.Endpoint.Environment.Website.IsEnabled
            && monitor.Endpoint.Environment.Website.DeletedAt == null
            && monitor.Endpoint.Environment.Website.Client.IsActive
            && monitor.Endpoint.Environment.Website.Client.DeletedAt == null
            && monitor.EndpointHealth != null
            && monitor.EndpointHealth.ConfirmedStatus == status
    };
}

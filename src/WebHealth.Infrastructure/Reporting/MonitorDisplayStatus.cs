using System.Linq.Expressions;
using WebHealth.Domain.Health;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Reporting;

/// <summary>
/// The status a monitor reports on the dashboard.
/// <para>
/// A disabled monitor keeps its last confirmed state in the database — that history is worth
/// keeping — but showing it as the current status says "Healthy" about an endpoint nobody is
/// checking, which is the most misleading thing a monitoring dashboard can say. §5.1 already has a
/// status for this, so a disabled monitor reports <see cref="EndpointHealthStatuses.Disabled" />
/// and the state it was in when it stopped is carried alongside rather than in place of it.
/// </para>
/// <para>
/// BR-U06 is not in tension with this: it decides <em>which samples</em> feed a figure — latest
/// confirmed state for health, all eligible samples for trends — not whether a lifecycle state may
/// take precedence over the last sample. Maintenance already works exactly this way.
/// </para>
/// <para>
/// The rule appears three times because three query shapes need it: the row, the status filter and
/// the health totals. They are kept side by side here, and
/// <c>MonitorDisplayStatusTests</c> compiles the expressions and asserts all three agree, so a
/// change to one that is not made to the others fails a test rather than making the page
/// contradict itself.
/// </para>
/// </summary>
public static class MonitorDisplayStatus
{
    /// <summary>The rule itself, for callers holding values rather than a query.</summary>
    public static string Of(bool isEnabled, string? confirmedStatus) =>
        !isEnabled ? EndpointHealthStatuses.Disabled
        : confirmedStatus ?? EndpointHealthStatuses.Unknown;

    /// <summary>The rule as a projection, for grouping the health totals in the database.</summary>
    public static Expression<Func<EndpointMonitor, string>> Projection => monitor =>
        !monitor.IsEnabled ? EndpointHealthStatuses.Disabled
        : monitor.EndpointHealth == null ? EndpointHealthStatuses.Unknown
        : monitor.EndpointHealth.ConfirmedStatus;

    /// <summary>
    /// The rule as a predicate, for the status filter. Written as a predicate rather than as a
    /// comparison against <see cref="Projection" /> because Entity Framework cannot compose one
    /// stored expression inside another.
    /// </summary>
    public static Expression<Func<EndpointMonitor, bool>> Matches(string status) => status switch
    {
        EndpointHealthStatuses.Disabled => monitor => !monitor.IsEnabled,

        // Unknown is the absence of a confirmation, so it is the one status that also matches a
        // monitor with no health row at all.
        EndpointHealthStatuses.Unknown => monitor => monitor.IsEnabled
            && (monitor.EndpointHealth == null
                || monitor.EndpointHealth.ConfirmedStatus == EndpointHealthStatuses.Unknown),

        _ => monitor => monitor.IsEnabled
            && monitor.EndpointHealth != null
            && monitor.EndpointHealth.ConfirmedStatus == status
    };
}

using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Health;

public sealed class IssueState
{
    public Guid Id { get; set; }
    public Guid EndpointMonitorId { get; set; }
    public required string IssueKey { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveRecoveries { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
}

public sealed class EndpointHealth
{
    public Guid EndpointMonitorId { get; set; }
    public Guid? EvidenceLogicalCheckId { get; set; }
    public required string ConfirmedStatus { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
    public long Version { get; set; }
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
    public LogicalCheck? EvidenceLogicalCheck { get; set; }
}

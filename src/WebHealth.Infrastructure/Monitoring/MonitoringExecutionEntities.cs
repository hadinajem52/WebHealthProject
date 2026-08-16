using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class LogicalCheck
{
    public Guid Id { get; set; }
    public Guid EndpointMonitorId { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public required string State { get; set; }
    public string? CadenceKey { get; set; }
    public required string PolicyFingerprint { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
    public ApplicationUser? InitiatedByUser { get; set; }
    public CheckConfigurationSnapshot ConfigurationSnapshot { get; set; } = null!;
    public ICollection<ExecutionAttempt> Attempts { get; } = [];
    public ICollection<DurableWork> DurableWork { get; } = [];
}

public sealed class CheckConfigurationSnapshot
{
    public Guid LogicalCheckId { get; set; }
    public short SchemaVersion { get; set; }
    public required string MonitorType { get; set; }
    public required string ConfigurationFingerprint { get; set; }
    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
    public int FailureConfirmationCount { get; set; }
    public int RecoveryConfirmationCount { get; set; }
    public int? WarningThresholdMs { get; set; }
    public int? CriticalThresholdMs { get; set; }
    public required string IntervalSource { get; set; }
    public required string TimeoutSource { get; set; }
    public required string ConfirmationSource { get; set; }
    public required string ThresholdSource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public LogicalCheck LogicalCheck { get; set; } = null!;
}

public sealed class ExecutionAttempt
{
    public Guid Id { get; set; }
    public Guid LogicalCheckId { get; set; }
    public int AttemptNumber { get; set; }
    public required string JobId { get; set; }
    public required string WorkerId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required string InfrastructureOutcome { get; set; }
    public string? FailureCategory { get; set; }
    public LogicalCheck LogicalCheck { get; set; } = null!;
}

public sealed class ExecutionLease
{
    public Guid EndpointMonitorId { get; set; }
    public Guid LogicalCheckId { get; set; }
    public Guid OwnerToken { get; set; }
    public long FencingGeneration { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
    public LogicalCheck LogicalCheck { get; set; } = null!;
}

public sealed class DurableWork
{
    public Guid Id { get; set; }
    public Guid LogicalCheckId { get; set; }
    public required string WorkKind { get; set; }
    public required string DedupeKey { get; set; }
    public required string QueueName { get; set; }
    public required string State { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public Guid? LeaseOwnerToken { get; set; }
    public DateTimeOffset? LeaseAcquiredAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastFailureCategory { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public LogicalCheck LogicalCheck { get; set; } = null!;
}

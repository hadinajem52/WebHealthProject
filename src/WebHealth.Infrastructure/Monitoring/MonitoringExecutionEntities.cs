using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Maintenance;
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
    public CheckResult? Result { get; set; }
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
    public string AcceptedStatusCodes { get; set; } = string.Empty;
    public string? RequiredContentMarker { get; set; }
    public string ContentMarkerComparison { get; set; } = "OrdinalIgnoreCase";
    public string ProductionHttpSeverity { get; set; } = "Warning";
    public int MaxResponseBodyBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxRedirects { get; set; } = 10;
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

public sealed class CheckResult
{
    public Guid LogicalCheckId { get; set; }
    public required string Outcome { get; set; }
    public string? FailureCategory { get; set; }
    public int? HttpStatus { get; set; }
    public int? DnsDurationMs { get; set; }
    public int? ConnectDurationMs { get; set; }
    public int? TlsDurationMs { get; set; }
    public int? TtfbDurationMs { get; set; }
    public int TotalDurationMs { get; set; }
    public long? TransferredLength { get; set; }
    public long? DecodedLength { get; set; }
    public string? LengthSource { get; set; }
    public bool ResponseTruncated { get; set; }
    public required string MonitorSource { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
    public Guid? MaintenanceOccurrenceId { get; set; }
    public bool IsMaintenance { get; set; }
    public bool CountsForUptime { get; set; }
    public string? SafeDiagnostic { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public LogicalCheck LogicalCheck { get; set; } = null!;
    public MaintenanceOccurrence? MaintenanceOccurrence { get; set; }
    public ICollection<RedirectHop> RedirectHops { get; } = [];
    public ICollection<Finding> Findings { get; } = [];
}

/// <summary>
/// One certificate inspection, keyed to the logical check that produced it. Holds public
/// certificate facts only (BR-C02): no private key material and no encoded certificate bytes.
/// </summary>
public sealed class CertificateObservation
{
    public Guid LogicalCheckId { get; set; }
    public Guid EndpointMonitorId { get; set; }
    public required string Subject { get; set; }
    public required string Issuer { get; set; }
    public required string SerialNumber { get; set; }
    public required string Sha256Fingerprint { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public int DaysRemaining { get; set; }
    public required string ValidationCategory { get; set; }
    public bool HostnameMatched { get; set; }
    public bool ChainTrusted { get; set; }
    public string? SubjectAlternativeNames { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public LogicalCheck LogicalCheck { get; set; } = null!;
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
}

public sealed class RedirectHop
{
    public Guid Id { get; set; }
    public Guid LogicalCheckId { get; set; }
    public int HopNumber { get; set; }
    public required string NormalizedFromUrl { get; set; }
    public required string NormalizedToUrl { get; set; }
    public int HttpStatus { get; set; }
    public bool IsLoop { get; set; }
    public CheckResult Result { get; set; } = null!;
}

public sealed class Finding
{
    public Guid Id { get; set; }
    public Guid LogicalCheckId { get; set; }
    public required string RuleKey { get; set; }
    public required string Severity { get; set; }
    public string? ObservedValue { get; set; }
    public string? ExpectedValue { get; set; }
    public required string IssueKey { get; set; }
    public CheckResult Result { get; set; } = null!;
}

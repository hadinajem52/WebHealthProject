namespace WebHealth.Application.Monitoring;

public interface ILogicalCheckFinalizationService
{
    Task<LogicalCheckFinalizationStatus> FinalizeAsync(
        FinalizeLogicalCheck command,
        CancellationToken cancellationToken = default);

    Task<LogicalCheckRetryStatus> PrepareRetryAsync(
        PrepareLogicalCheckRetry command,
        CancellationToken cancellationToken = default);
}

public sealed record FinalizeLogicalCheck(
    ExecutionLeaseClaim Lease,
    Guid AttemptId,
    Guid DurableWorkId,
    LogicalCheckTerminalEvidence Evidence);

public abstract record LogicalCheckTerminalEvidence;

public sealed record HttpTransportEvidence(
    SafeHttpTransportRequest Request,
    SafeHttpTransportResult Result) : LogicalCheckTerminalEvidence;

public sealed record SslCertificateEvidence(
    SslCertificateProbeRequest Request,
    SslCertificateProbeResult Result) : LogicalCheckTerminalEvidence;

public sealed record ExecutionTerminalEvidence(
    ExecutionTerminalReason Reason) : LogicalCheckTerminalEvidence;

public enum ExecutionTerminalReason
{
    TargetIneligible,
    RetriesExhausted
}

public sealed record PrepareLogicalCheckRetry(
    ExecutionLeaseClaim Lease,
    Guid AttemptId,
    Guid DurableWorkId,
    string FailureCategory);

public enum LogicalCheckFinalizationStatus
{
    Finalized,
    AlreadyFinalized,
    LeaseLost,
    InvalidLogicalCheck,
    TargetMismatch,
    PolicyMismatch,
    InvalidTransportResult,
    InvalidExecutionAttempt,
    InvalidDurableWork
}

public enum LogicalCheckRetryStatus
{
    RetryPrepared,
    Superseded,
    LeaseLost,
    AlreadyFinalized,
    InvalidExecutionAttempt,
    InvalidDurableWork
}

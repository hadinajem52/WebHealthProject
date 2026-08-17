namespace WebHealth.Application.Monitoring;

public interface IHttpCheckHistoryService
{
    Task<HttpCheckHistoryWriteStatus> RecordAsync(
        RecordHttpCheckHistory request,
        CancellationToken cancellationToken = default);
}

public sealed record RecordHttpCheckHistory(
    ExecutionLeaseClaim Lease,
    SafeHttpTransportRequest Request,
    SafeHttpTransportResult Transport,
    FinalizeExecutionAttempt? Attempt = null);

public sealed record FinalizeExecutionAttempt(
    Guid AttemptId,
    string Outcome,
    string? FailureCategory = null);

public enum HttpCheckHistoryWriteStatus
{
    Recorded,
    AlreadyRecorded,
    LeaseLost,
    InvalidLogicalCheck,
    TargetMismatch,
    PolicyMismatch,
    InvalidTransportResult,
    InvalidExecutionAttempt
}

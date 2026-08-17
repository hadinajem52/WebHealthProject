namespace WebHealth.Application.Monitoring;

public interface ILogicalCheckExecutionService
{
    Task<LogicalCheckExecutionStatus> ExecuteAsync(
        ExecuteLogicalCheck command,
        CancellationToken cancellationToken = default);
}

public sealed record ExecuteLogicalCheck(
    Guid LogicalCheckId,
    string JobId,
    string WorkerId,
    bool IsFinalAttempt);

public enum LogicalCheckExecutionStatus
{
    Completed,
    AlreadyCompleted,
    LeaseUnavailable,
    RetryRequired
}

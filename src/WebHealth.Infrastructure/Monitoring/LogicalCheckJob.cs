using Hangfire;
using Hangfire.Server;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class LogicalCheckJob(ILogicalCheckExecutionService executionService)
{
    private const int MaximumRetries = 2;

    [Queue(MonitoringQueueNames.ShortChecks)]
    [AutomaticRetry(Attempts = MaximumRetries, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(
        Guid logicalCheckId,
        Guid durableWorkId,
        PerformContext context,
        CancellationToken cancellationToken)
    {
        var status = await executionService.ExecuteAsync(new(
            logicalCheckId,
            durableWorkId,
            context.BackgroundJob.Id,
            context.ServerId), cancellationToken);
        if (status is LogicalCheckExecutionStatus.RetryRequired
            or LogicalCheckExecutionStatus.ReconciliationRequired)
        {
            throw new LogicalCheckRetryRequiredException(status);
        }
    }
}

internal sealed class LogicalCheckRetryRequiredException(LogicalCheckExecutionStatus status)
    : Exception(status == LogicalCheckExecutionStatus.ReconciliationRequired
        ? "The logical check requires lease reconciliation."
        : "The logical check requires another infrastructure attempt.");

using Hangfire;
using Hangfire.Server;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class LogicalCheckJob(ILogicalCheckExecutionService executionService)
{
    private const int MaximumRetries = 2;

    [Queue("monitoring")]
    [AutomaticRetry(Attempts = MaximumRetries, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(
        Guid logicalCheckId,
        PerformContext context,
        CancellationToken cancellationToken)
    {
        var retryCount = context.GetJobParameter<int>("RetryCount");
        var status = await executionService.ExecuteAsync(new(
            logicalCheckId,
            context.BackgroundJob.Id,
            context.ServerId,
            retryCount >= MaximumRetries), cancellationToken);
        if (status == LogicalCheckExecutionStatus.RetryRequired)
        {
            throw new LogicalCheckRetryRequiredException();
        }
    }
}

internal sealed class LogicalCheckRetryRequiredException()
    : Exception("The logical check requires another infrastructure attempt.");

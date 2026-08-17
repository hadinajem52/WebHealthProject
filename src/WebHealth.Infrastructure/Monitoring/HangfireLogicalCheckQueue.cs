using Hangfire;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class HangfireLogicalCheckQueue(IBackgroundJobClient backgroundJobs)
    : ILogicalCheckQueue
{
    public string Enqueue(Guid logicalCheckId, Guid durableWorkId) =>
        backgroundJobs.Enqueue<LogicalCheckJob>(job => job.ExecuteAsync(
            logicalCheckId,
            durableWorkId,
            null!,
            CancellationToken.None));
}

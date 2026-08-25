using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal static class DurableWorkEnqueueAcknowledgement
{
    private static readonly TimeSpan AcknowledgeTimeout = TimeSpan.FromSeconds(5);

    public static async Task<bool> TryEnqueueAsync(
        ApplicationDbContext dbContext,
        ILogicalCheckQueue logicalCheckQueue,
        TimeProvider timeProvider,
        ILogger logger,
        Guid logicalCheckId,
        Guid durableWorkId)
    {
        try
        {
            logicalCheckQueue.Enqueue(logicalCheckId, durableWorkId);
            using var acknowledgeTimeout = new CancellationTokenSource(AcknowledgeTimeout);
            await AcknowledgeAsync(dbContext, timeProvider, durableWorkId, acknowledgeTimeout.Token);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Logical check enqueue was interrupted for {LogicalCheckId} and will be reconciled.",
                logicalCheckId);
            return false;
        }
    }

    private static async Task AcknowledgeAsync(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        Guid workId,
        CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var updated = await dbContext.DurableWork
            .Where(work => work.Id == workId && work.State == DurableWorkStates.Dispatching)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(work => work.State, DurableWorkStates.Enqueued)
                .SetProperty(work => work.UpdatedAt, now), token);
        if (updated != 1)
        {
            return;
        }

        var tracked = dbContext.DurableWork.Local.SingleOrDefault(work => work.Id == workId);
        if (tracked is null)
        {
            return;
        }

        tracked.State = DurableWorkStates.Enqueued;
        tracked.UpdatedAt = now;
        var entry = dbContext.Entry(tracked);
        entry.Property(work => work.State).OriginalValue = DurableWorkStates.Enqueued;
        entry.Property(work => work.State).IsModified = false;
        entry.Property(work => work.UpdatedAt).OriginalValue = now;
        entry.Property(work => work.UpdatedAt).IsModified = false;
    }
}

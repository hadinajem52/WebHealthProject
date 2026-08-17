using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

/// <summary>
/// Shared by scheduled dispatch and manual checks: enqueues one piece of durable work and,
/// only if it is still Dispatching, acknowledges it as Enqueued. The acknowledgement uses its
/// own short-lived timeout rather than the caller's token, because once work is committed as
/// Dispatching, enqueueing is the operation's success boundary — a cancelled caller must not be
/// able to leave a durable row stuck behind an unacknowledged, already-delivered job.
/// </summary>
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
        // Only transitions a row that is still Dispatching. A worker may already have raced ahead
        // to Processing or Completed by the time this runs; that state must never be overwritten.
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

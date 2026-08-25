using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class DisabledLogicalCheckQueue : ILogicalCheckQueue
{
    public string Enqueue(Guid logicalCheckId, Guid durableWorkId) =>
        throw new InvalidOperationException(
            "Monitoring scheduling is disabled; no queue is available to enqueue logical checks.");
}

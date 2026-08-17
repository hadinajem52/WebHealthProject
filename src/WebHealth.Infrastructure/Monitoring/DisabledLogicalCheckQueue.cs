using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

/// <summary>
/// Registered when Monitoring:Scheduling is disabled, so that ILogicalCheckQueue always has an
/// implementation available and controllers that depend on it (transitively, via IManualCheckService)
/// keep resolving. Callers must check scheduling availability before enqueueing; this exists as a
/// safety net, not a supported way to queue work.
/// </summary>
internal sealed class DisabledLogicalCheckQueue : ILogicalCheckQueue
{
    public string Enqueue(Guid logicalCheckId, Guid durableWorkId) =>
        throw new InvalidOperationException(
            "Monitoring scheduling is disabled; no queue is available to enqueue logical checks.");
}

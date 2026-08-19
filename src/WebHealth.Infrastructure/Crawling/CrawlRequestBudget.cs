using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// The crawler's share of the shared outbound-request budget, held **process-wide** rather than per
/// run. Per-run limits alone do not isolate anything: several runs each staying under their own
/// budget can still fill every slot of the transport's global limiter between them, and scheduled
/// checks would then block at the transport rather than at the queue — exactly the starvation the
/// separate Hangfire queue exists to prevent.
/// <para>
/// Sized at half the transport's global concurrency, so at least half of it is always available to
/// monitoring however many crawls are running.
/// </para>
/// </summary>
internal sealed class CrawlRequestBudget
{
    private readonly SemaphoreSlim _slots;

    public CrawlRequestBudget(SafeHttpTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(transportOptions);
        Capacity = Math.Max(1, transportOptions.GlobalConcurrency / 2);
        _slots = new(Capacity, Capacity);
    }

    public int Capacity { get; }

    public int Available => _slots.CurrentCount;

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        return new Slot(_slots);
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private SemaphoreSlim? _slots = slots;

        public void Dispose() => Interlocked.Exchange(ref _slots, null)?.Release();
    }
}

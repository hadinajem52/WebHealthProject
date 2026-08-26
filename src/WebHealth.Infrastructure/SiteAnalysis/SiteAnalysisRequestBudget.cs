using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.SiteAnalysis;

internal sealed class SiteAnalysisRequestBudget
{
    private readonly SemaphoreSlim _slots;

    public SiteAnalysisRequestBudget(SafeHttpTransportOptions transportOptions)
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

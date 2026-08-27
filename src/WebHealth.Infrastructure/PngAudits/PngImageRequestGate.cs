namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngImageRequestGate : IDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);

    internal int Available => _slot.CurrentCount;

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _slot.WaitAsync(cancellationToken);
        return new Lease(_slot);
    }

    public void Dispose() => _slot.Dispose();

    private sealed class Lease(SemaphoreSlim slot) : IDisposable
    {
        private SemaphoreSlim? _slot = slot;

        public void Dispose() => Interlocked.Exchange(ref _slot, null)?.Release();
    }
}

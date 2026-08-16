namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SafeHttpConcurrencyLimiter(SafeHttpTransportOptions options)
{
    private readonly SemaphoreSlim _global = new(options.GlobalConcurrency, options.GlobalConcurrency);
    private readonly KeyedLimiter _hosts = new(options.PerHostConcurrency);
    private readonly KeyedLimiter _addresses = new(options.PerIpConcurrency);

    public async ValueTask<IDisposable> AcquireGlobalAsync(CancellationToken cancellationToken)
    {
        await _global.WaitAsync(cancellationToken);
        return new ReleaseAction(() => _global.Release());
    }

    public ValueTask<IDisposable> AcquireHostAsync(string host, CancellationToken cancellationToken) =>
        _hosts.AcquireAsync(host, cancellationToken);

    public ValueTask<IDisposable> AcquireAddressAsync(string address, CancellationToken cancellationToken) =>
        _addresses.AcquireAsync(address, cancellationToken);

    private sealed class KeyedLimiter(int concurrency)
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
        {
            Entry entry;
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new Entry(concurrency);
                    _entries.Add(key, entry);
                }

                entry.Users++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new ReleaseAction(() => Release(key, entry, acquired: true));
            }
            catch
            {
                Release(key, entry, acquired: false);
                throw;
            }
        }

        private void Release(string key, Entry entry, bool acquired)
        {
            if (acquired)
            {
                entry.Semaphore.Release();
            }

            lock (_lock)
            {
                entry.Users--;
                if (entry.Users == 0)
                {
                    _entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class Entry(int concurrency)
        {
            public SemaphoreSlim Semaphore { get; } = new(concurrency, concurrency);
            public int Users { get; set; }
        }
    }

    private sealed class ReleaseAction(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

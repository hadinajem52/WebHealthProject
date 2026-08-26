namespace WebHealth.Infrastructure.SiteAnalysis;

internal sealed class SiteAnalysisHostRateLimiter(TimeProvider timeProvider) : IDisposable
{
    internal const int MaximumTrackedHosts = 4096;

    private readonly Dictionary<string, HostState> _hosts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lock = new();

    internal int TrackedHostCount
    {
        get
        {
            lock (_lock) return _hosts.Count;
        }
    }

    public async Task WaitAsync(
        string host,
        double requestsPerSecondPerHost,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (requestsPerSecondPerHost == 0) return;
        if (!double.IsFinite(requestsPerSecondPerHost)
            || requestsPerSecondPerHost is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(requestsPerSecondPerHost));
        }

        var interval = TimeSpan.FromSeconds(1 / requestsPerSecondPerHost);
        var state = Rent(host);
        try
        {
            await AdmitAsync(state, interval, cancellationToken);
        }
        finally
        {
            Return(host, state);
        }
    }

    private async Task AdmitAsync(
        HostState state,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var delay = state.LastAdmitted is { } lastAdmitted
                ? lastAdmitted + interval - now
                : TimeSpan.Zero;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            state.LastAdmitted = timeProvider.GetUtcNow();
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private HostState Rent(string host)
    {
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (!_hosts.TryGetValue(host, out var state))
            {
                EnsureCapacity();
                state = new HostState(now);
                _hosts.Add(host, state);
            }

            state.References++;
            state.LastUsed = now;
            return state;
        }
    }

    private void Return(string host, HostState state)
    {
        lock (_lock)
        {
            state.References--;
            state.LastUsed = timeProvider.GetUtcNow();
            if (state.References == 0 && state.LastAdmitted is null)
            {
                state.Gate.Dispose();
                _hosts.Remove(host);
            }
        }
    }

    private void EnsureCapacity()
    {
        if (_hosts.Count < MaximumTrackedHosts) return;
        var oldestIdle = _hosts
            .Where(entry => entry.Value.References == 0)
            .OrderBy(entry => entry.Value.LastUsed)
            .Take(1)
            .ToArray();
        if (oldestIdle.Length == 0)
        {
            throw new InvalidOperationException("The site-analysis host limit is exhausted.");
        }

        oldestIdle[0].Value.Gate.Dispose();
        _hosts.Remove(oldestIdle[0].Key);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var state in _hosts.Values)
            {
                state.Gate.Dispose();
            }

            _hosts.Clear();
        }
    }

    private sealed class HostState(DateTimeOffset now)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTimeOffset? LastAdmitted { get; set; }

        public DateTimeOffset LastUsed { get; set; } = now;

        public int References { get; set; }
    }
}

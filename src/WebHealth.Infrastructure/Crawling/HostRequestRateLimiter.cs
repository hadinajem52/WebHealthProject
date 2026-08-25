namespace WebHealth.Infrastructure.Crawling;

internal sealed class HostRequestRateLimiter(TimeProvider timeProvider, double requestsPerSecondPerHost)
{
    private readonly TimeSpan _interval = requestsPerSecondPerHost <= 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(1 / requestsPerSecondPerHost);

    private readonly Dictionary<string, DateTimeOffset> _nextAllowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public async Task WaitAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_interval == TimeSpan.Zero) return;

        TimeSpan delay;
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            var earliest = _nextAllowed.TryGetValue(host, out var scheduled) && scheduled > now ? scheduled : now;
            _nextAllowed[host] = earliest + _interval;
            delay = earliest - now;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }
}

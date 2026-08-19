namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// BR-L05. Bounds how fast requests to one host may **start**, which is a different control from
/// how many may be in flight. Concurrency alone lets two workers hammer a host as fast as it can
/// answer; a rate limit alone lets an unbounded number pile up. A target host needs both.
/// <para>
/// Spacing is tracked as the next instant a request to that host may begin, advanced under a lock
/// and then awaited outside it, so callers queue rather than contend.
/// </para>
/// </summary>
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

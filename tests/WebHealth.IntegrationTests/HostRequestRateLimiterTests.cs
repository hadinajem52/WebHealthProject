using System.Diagnostics;
using FluentAssertions;
using WebHealth.Infrastructure.Crawling;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// BR-L05. Concurrency bounds how many requests are in flight; the rate limit bounds how fast they
/// start. A target host needs both, and only the second is tested here.
/// </summary>
public sealed class HostRequestRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_SpacesConsecutiveRequestsToOneHost()
    {
        var limiter = new HostRequestRateLimiter(TimeProvider.System, requestsPerSecondPerHost: 20);
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 5; index++)
        {
            await limiter.WaitAsync("site.test", CancellationToken.None);
        }

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150),
            "five requests at twenty a second span at least four intervals");
    }

    [Fact]
    public async Task WaitAsync_TracksHostsSeparately()
    {
        var limiter = new HostRequestRateLimiter(TimeProvider.System, requestsPerSecondPerHost: 5);
        await limiter.WaitAsync("a.test", CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await limiter.WaitAsync("b.test", CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(150),
            "one slow host must not throttle every other host in the run");
    }

    [Fact]
    public async Task WaitAsync_QueuesConcurrentCallersRatherThanLettingThemPassTogether()
    {
        var limiter = new HostRequestRateLimiter(TimeProvider.System, requestsPerSecondPerHost: 10);
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => limiter.WaitAsync("site.test", CancellationToken.None)));

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250),
            "four concurrent callers still span three intervals");
    }

    [Fact]
    public async Task WaitAsync_DoesNothingWhenTheRateIsDisabled()
    {
        var limiter = new HostRequestRateLimiter(TimeProvider.System, requestsPerSecondPerHost: 0);
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 50; index++)
        {
            await limiter.WaitAsync("site.test", CancellationToken.None);
        }

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }
}

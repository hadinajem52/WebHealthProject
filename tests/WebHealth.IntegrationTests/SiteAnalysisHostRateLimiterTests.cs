using System.Diagnostics;
using FluentAssertions;
using WebHealth.Infrastructure.SiteAnalysis;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class SiteAnalysisHostRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_SpacesConsecutiveRequestsToOneHost()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 5; index++)
        {
            await limiter.WaitAsync("site.test", 10, CancellationToken.None);
        }

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(300),
            "five requests at ten a second span at least four intervals");
    }

    [Fact]
    public async Task WaitAsync_TracksHostsSeparately()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);
        await limiter.WaitAsync("a.test", 5, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await limiter.WaitAsync("b.test", 5, CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(150),
            "one slow host must not throttle every other host in the run");
    }

    [Fact]
    public async Task WaitAsync_QueuesConcurrentCallersRatherThanLettingThemPassTogether()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => limiter.WaitAsync("site.test", 10, CancellationToken.None)));

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250),
            "four concurrent callers still span three intervals");
    }

    [Fact]
    public async Task WaitAsync_DoesNothingWhenTheRateIsDisabled()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 50; index++)
        {
            await limiter.WaitAsync("site.test", 0, CancellationToken.None);
        }

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task CancelledWait_DoesNotReserveAnotherInterval()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);
        await limiter.WaitAsync("site.test", 2, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => limiter.WaitAsync("site.test", 2, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await Task.Delay(TimeSpan.FromMilliseconds(550));
        var stopwatch = Stopwatch.StartNew();
        await limiter.WaitAsync("site.test", 2, CancellationToken.None);
        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task WaitAsync_AppliesTheCurrentConsumerRate()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);
        await limiter.WaitAsync("site.test", 10, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await limiter.WaitAsync("site.test", 2, CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task WaitAsync_BoundsRetainedHostState()
    {
        var limiter = new SiteAnalysisHostRateLimiter(TimeProvider.System);

        for (var index = 0; index <= SiteAnalysisHostRateLimiter.MaximumTrackedHosts; index++)
        {
            await limiter.WaitAsync($"host-{index}.test", 10, CancellationToken.None);
        }

        limiter.TrackedHostCount.Should().BeLessOrEqualTo(
            SiteAnalysisHostRateLimiter.MaximumTrackedHosts);
    }
}

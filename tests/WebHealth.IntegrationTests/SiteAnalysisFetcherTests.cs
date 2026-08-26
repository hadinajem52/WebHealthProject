using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.SiteAnalysis;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class SiteAnalysisFetcherTests
{
    [Fact]
    public async Task FetchAsync_RetriesTransientResponsesAndReportsAttempts()
    {
        var transport = new SequenceTransport(Response(503), Response(200));
        var transportOptions = new SafeHttpTransportOptions();
        var fetcher = new SiteAnalysisFetcher(
            transport,
            new SiteAnalysisRequestBudget(transportOptions),
            new SiteAnalysisHostRateLimiter(TimeProvider.System),
            TimeProvider.System);
        var request = new SiteAnalysisFetchRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://site.test/page",
            false);
        var profile = new SiteAnalysisFetchProfile(1024, 15, 0, 1, TimeSpan.Zero, TimeSpan.Zero);

        var result = await fetcher.FetchAsync(request, profile);

        result.Attempts.Should().Be(2);
        result.Response.StatusCode.Should().Be(200);
        transport.RequestCount.Should().Be(2);
    }

    private static SafeHttpTransportResult Response(int statusCode) => new(
        null,
        statusCode,
        new("https://site.test/page"),
        TimeSpan.Zero,
        0,
        false,
        ReadOnlyMemory<byte>.Empty,
        []);

    private sealed class SequenceTransport(params SafeHttpTransportResult[] responses)
        : ISafeHttpTransport
    {
        private int _next;

        public int RequestCount => _next;

        public Task<SafeHttpTransportResult> SendAsync(
            SafeHttpTransportRequest request,
            CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _next) - 1;
            return Task.FromResult(responses[Math.Min(index, responses.Length - 1)]);
        }
    }
}

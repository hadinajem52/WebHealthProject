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
        result.OutboundRequestCount.Should().Be(2);
        result.Response.StatusCode.Should().Be(200);
        transport.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task FetchAsync_EnforcesTheOutboundRequestBudgetAcrossRetriesAndRedirects()
    {
        var transport = new SequenceTransport(
            Response(503) with { OutboundRequestCount = 2 },
            Response(503) with { OutboundRequestCount = 1 });
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
            false)
        {
            MaxOutboundRequests = 3
        };
        var profile = new SiteAnalysisFetchProfile(1024, 15, 0, 3, TimeSpan.Zero, TimeSpan.Zero);

        var result = await fetcher.FetchAsync(request, profile);

        result.OutboundRequestCount.Should().Be(3);
        result.OutboundRequestLimitReached.Should().BeTrue();
        result.Attempts.Should().Be(2);
        transport.RequestedRedirectLimits.Should().Equal(2, 0);
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

        public List<int> RequestedRedirectLimits { get; } = [];

        public int RequestCount => _next;

        public Task<SafeHttpTransportResult> SendAsync(
            SafeHttpTransportRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedRedirectLimits.Add(request.MaxRedirects);
            var index = Interlocked.Increment(ref _next) - 1;
            return Task.FromResult(responses[Math.Min(index, responses.Length - 1)]);
        }
    }
}

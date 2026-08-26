using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.SiteAnalysis;

namespace WebHealth.IntegrationTests.Support;

internal sealed class FakeSiteTransport : ISafeHttpTransport
{
    private readonly Dictionary<string, SiteResponse> _pages = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Requested { get; } = new();

    public int MaxObservedConcurrency { get; private set; }

    public Func<string, Task>? BeforeRespondAsync { get; set; }

    private int _inFlight;
    private readonly Lock _lock = new();

    public sealed record SiteResponse(
        int? StatusCode,
        string? Html = null,
        SafeHttpFailureKind? Failure = null,
        int RedirectCount = 0,
        string? FinalUrl = null,
        bool Truncated = false,
        TimeSpan? RetryAfter = null,
        string? DisplayUrl = null);

    public FakeSiteTransport Page(string url, string html) =>
        With(url, new(200, html));

    public FakeSiteTransport Status(string url, int statusCode) =>
        With(url, new(statusCode));

    public FakeSiteTransport Failing(string url, SafeHttpFailureKind failure) =>
        With(url, new(null, Failure: failure));

    public FakeSiteTransport With(string url, SiteResponse response)
    {
        _pages[url] = response;
        return this;
    }

    public async Task<SafeHttpTransportResult> SendAsync(
        SafeHttpTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        Requested.Enqueue(request.Url);
        lock (_lock)
        {
            _inFlight++;
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, _inFlight);
        }

        try
        {
            if (BeforeRespondAsync is not null) await BeforeRespondAsync(request.Url);
            cancellationToken.ThrowIfCancellationRequested();

            var response = _pages.GetValueOrDefault(request.Url, new SiteResponse(404));
            var finalUrl = response.FinalUrl ?? request.Url;
            if (response.RedirectCount > request.MaxRedirects)
            {
                return new(
                    SafeHttpFailureKind.RedirectLimit,
                    302,
                    new SafeHttpDestination(request.Url),
                    TimeSpan.FromMilliseconds(5),
                    0,
                    false,
                    ReadOnlyMemory<byte>.Empty,
                    [.. Enumerable.Range(0, request.MaxRedirects)
                        .Select(index => new SafeHttpRedirectHop(301, request.Url, request.Url, false))])
                {
                    FinalRequestUrl = request.Url,
                    OutboundRequestCount = request.MaxRedirects + 1
                };
            }
            if (response.RedirectCount > 0
                && request.HopPolicy is not null
                && await request.HopPolicy.EvaluateAsync(
                    new(finalUrl, response.RedirectCount), cancellationToken) is { Allowed: false } decision)
            {
                return new(
                    SafeHttpFailureKind.RequestPolicyRejected,
                    null,
                    new SafeHttpDestination(finalUrl),
                    TimeSpan.FromMilliseconds(5),
                    0,
                    false,
                    ReadOnlyMemory<byte>.Empty,
                    [new SafeHttpRedirectHop(302, request.Url, finalUrl, false)],
                    PolicyRejectionReason: decision.RejectionReason)
                {
                    FinalRequestUrl = request.Url,
                    OutboundRequestCount = 1
                };
            }
            var body = response.Html is null
                ? ReadOnlyMemory<byte>.Empty
                : Encoding.UTF8.GetBytes(response.Html);

            return new(
                response.Failure,
                response.StatusCode,
                new SafeHttpDestination(response.DisplayUrl ?? finalUrl),
                TimeSpan.FromMilliseconds(5),
                body.Length,
                response.Truncated,
                body,
                [.. Enumerable.Range(0, response.RedirectCount)
                    .Select(index => new SafeHttpRedirectHop(301, request.Url, request.Url, false))],
                ContentType: response.Html is null ? null : "text/html; charset=utf-8",
                RetryAfter: response.RetryAfter)
            {
                FinalRequestUrl = finalUrl,
                OutboundRequestCount = response.RedirectCount + 1
            };
        }
        finally
        {
            lock (_lock) _inFlight--;
        }
    }
}

internal sealed class FakeRobotsReader(CrawlRobotsFacts? facts = null) : ICrawlRobotsReader
{
    public Task<CrawlRobotsFacts> GetAsync(string origin, CancellationToken cancellationToken = default) =>
        Task.FromResult(facts ?? CrawlRobotsFacts.Unknown);
}

internal sealed class PerOriginRobotsReader(Dictionary<string, CrawlRobotsFacts> factsByOrigin)
    : ICrawlRobotsReader
{
    public Task<CrawlRobotsFacts> GetAsync(string origin, CancellationToken cancellationToken = default) =>
        Task.FromResult(factsByOrigin.GetValueOrDefault(origin, CrawlRobotsFacts.Unknown));
}

internal static class CrawlTestHarness
{
    public const string Seed = "https://site.test/";

    public static CrawlSchedulingOptions Options => new()
    {
        Enabled = true,
        RequestConcurrency = 1,
        RequestsPerSecondPerHost = 0,
        MaxDuration = TimeSpan.FromMinutes(5),
        FetchTimeoutSeconds = 5,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryDelay = TimeSpan.Zero,
        MaxPageBytes = 256 * 1024
    };

    public static string LinkTo(params string[] hrefs) =>
        "<!doctype html><html><body>"
        + string.Concat(hrefs.Select(href => $"<a href=\"{href}\">x</a>"))
        + "</body></html>";

    public static async Task<(CrawlRunOutcome Outcome, RecordingCrawlResultSink Sink)> RunAsync(
        FakeSiteTransport transport,
        CrawlRunRequest request,
        CrawlSchedulingOptions? options = null,
        ICrawlRobotsReader? robotsReader = null,
        SiteAnalysisRequestBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        var sink = new RecordingCrawlResultSink();
        var transportOptions = new SafeHttpTransportOptions();
        var effective = options ?? Options;
        var timeProvider = TimeProvider.System;
        var discoveryExtractor = new HtmlDocumentDiscoveryExtractor();
        var fetcher = new SiteAnalysisFetcher(
            transport,
            budget ?? new SiteAnalysisRequestBudget(transportOptions),
            new SiteAnalysisHostRateLimiter(timeProvider),
            timeProvider);
        var service = new CrawlExecutionService(
            fetcher,
            new HtmlLinkExtractor(discoveryExtractor),
            robotsReader ?? new FakeRobotsReader(),
            sink,
            effective,
            transportOptions,
            timeProvider,
            NullLogger<CrawlExecutionService>.Instance);

        var outcome = await service.ExecuteAsync(request, cancellationToken);
        outcome.Should().NotBeNull("the harness's sink grants the claim, so the run is performed");
        return (outcome, sink);
    }

    public static CrawlRunRequest Request(params string[] seeds) =>
        new(Guid.NewGuid(), Guid.NewGuid(), IsProduction: false, seeds.Length == 0 ? [Seed] : seeds);
}

using System.Collections.Concurrent;
using System.Text;
using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// A controlled mini-site the crawl execution tests drive, standing in for the real transport. It
/// is the same shape as the fixture site Phase 0 planned: working, redirected and broken links, all
/// answered without a network.
/// </summary>
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
        string? FinalUrl = null);

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

            // An unconfigured URL is a 404, which is what makes a link to a page the fixture never
            // defined a broken link rather than a silent success.
            var response = _pages.GetValueOrDefault(request.Url, new SiteResponse(404));
            var body = response.Html is null
                ? ReadOnlyMemory<byte>.Empty
                : Encoding.UTF8.GetBytes(response.Html);

            return new(
                response.Failure,
                response.StatusCode,
                new SafeHttpDestination(response.FinalUrl ?? request.Url),
                TimeSpan.FromMilliseconds(5),
                body.Length,
                false,
                body,
                [.. Enumerable.Range(0, response.RedirectCount)
                    .Select(index => new SafeHttpRedirectHop(301, request.Url, request.Url, false))],
                ContentType: response.Html is null ? null : "text/html; charset=utf-8");
        }
        finally
        {
            lock (_lock) _inFlight--;
        }
    }
}

/// <summary>Authorizes every host by default, so a test opts in to refusing one.</summary>
internal sealed class FakeTargetAuthorizer(params string[] deniedHosts) : IMonitoringTargetAuthorizer
{
    public Task<bool> IsAuthorizedAsync(
        Guid endpointId,
        string normalizedHost,
        int port,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(!deniedHosts.Contains(normalizedHost, StringComparer.OrdinalIgnoreCase));
}

internal sealed class FakeRobotsReader(CrawlRobotsFacts? facts = null) : ICrawlRobotsReader
{
    public Task<CrawlRobotsFacts> GetAsync(string origin, CancellationToken cancellationToken = default) =>
        Task.FromResult(facts ?? CrawlRobotsFacts.Unknown);
}

/// <summary>Different robots facts per origin, for runs that span more than one seed origin.</summary>
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
        IMonitoringTargetAuthorizer? authorizer = null,
        CancellationToken cancellationToken = default)
    {
        var sink = new RecordingCrawlResultSink();
        var service = new CrawlExecutionService(
            transport,
            new HtmlLinkExtractor(),
            robotsReader ?? new FakeRobotsReader(),
            sink,
            authorizer ?? new FakeTargetAuthorizer(),
            options ?? Options,
            new SafeHttpTransportOptions(),
            TimeProvider.System);

        var outcome = await service.ExecuteAsync(request, cancellationToken);
        return (outcome, sink);
    }

    public static CrawlRunRequest Request(params string[] seeds) =>
        new(Guid.NewGuid(), Guid.NewGuid(), IsProduction: false, seeds.Length == 0 ? [Seed] : seeds);
}

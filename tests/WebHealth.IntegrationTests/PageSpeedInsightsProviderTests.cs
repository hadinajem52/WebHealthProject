using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.PageAudits;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// The provider adapter against recorded responses and a fake handler. Nothing here reaches
/// Google: the fixtures are the contract, and a live call would make the suite depend on somebody
/// else's uptime, quota and current audit set.
/// </summary>
public sealed class PageSpeedInsightsProviderTests
{
    private const string ApiKey = "test-key-must-never-be-logged";

    private static readonly PageAuditRequest Request = new(
        new Uri("https://example.com/"),
        PageAuditCategories.Seo,
        PageAuditStrategies.Mobile,
        "en-US");

    [Fact]
    public async Task RunAsync_ReadsAScoreAndItsAuditsFromARecordedResponse()
    {
        var (provider, handler) = Create(Fixture("success-seo-mobile.json"));

        var result = await provider.RunAsync(Request);

        result.Provider.Should().Be(PageAuditProviders.PageSpeedInsights);
        result.CategoryScore.Should().Be(0.8m);
        result.LighthouseVersion.Should().Be("11.4.0");
        result.RequestedUrl.Should().Be("https://example.com/");
        result.FinalUrl.Should().Be("https://example.com/");
        result.AnalysisAt.Should().Be(DateTimeOffset.Parse("2026-08-19T09:14:52.331Z"));
        result.Warnings.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
    }

    /// <summary>
    /// Membership comes from the category's auditRefs. The fixture carries an audit belonging to
    /// another category, and counting it would attribute it to a score it took no part in.
    /// </summary>
    [Fact]
    public async Task RunAsync_TakesOnlyTheAuditsTheSeoCategoryReferences()
    {
        var (provider, _) = Create(Fixture("success-seo-mobile.json"));

        var result = await provider.RunAsync(Request);

        result.Items.Should().HaveCount(12);
        result.Items.Select(item => item.AuditId).Should().NotContain("first-contentful-paint",
            "the response carries it, but the SEO category does not reference it");
        result.Items.Select(item => item.AuditId).Should().Contain("meta-description");
    }

    [Fact]
    public async Task RunAsync_CarriesTheWeightAndGroupFromTheReferenceNotTheAudit()
    {
        var (provider, _) = Create(Fixture("success-seo-mobile.json"));

        var result = await provider.RunAsync(Request);

        var structuredData = result.Items.Single(item => item.AuditId == "structured-data");
        structuredData.Weight.Should().Be(0, "a manual audit contributes nothing to the score");
        structuredData.ScoreDisplayMode.Should().Be("manual");

        var metaDescription = result.Items.Single(item => item.AuditId == "meta-description");
        metaDescription.Weight.Should().Be(10);
        metaDescription.Group.Should().Be("seo-content");
        metaDescription.Score.Should().Be(0m);
    }

    [Fact]
    public async Task RunAsync_KeepsTheProviderTextAnAuditCarries()
    {
        var (provider, _) = Create(Fixture("success-seo-mobile.json"));

        var result = await provider.RunAsync(Request);

        var linkText = result.Items.Single(item => item.AuditId == "link-text");
        linkText.DisplayValue.Should().Be("3 links found");
        linkText.Explanation.Should().StartWith("Anchor text");
        linkText.Title.Should().Be("Links do not have descriptive text");
    }

    [Fact]
    public async Task RunAsync_SeparatesManualNotApplicableInformativeAndErroredAudits()
    {
        var (provider, _) = Create(Fixture("manual-and-na.json"));

        var result = await provider.RunAsync(Request);

        Mode(result, "structured-data").Should().Be("manual");
        Mode(result, "robots-txt").Should().Be("notApplicable");
        Mode(result, "font-size").Should().Be("informative");
        result.Items.Single(item => item.AuditId == "canonical").ErrorMessage
            .Should().Be("Required Canonical gatherer did not run.");
    }

    [Fact]
    public async Task RunAsync_ReportsTheRunWarningsTheProviderSent()
    {
        var (provider, _) = Create(Fixture("manual-and-na.json"));

        var result = await provider.RunAsync(Request);

        result.Warnings.Should().HaveCount(2);
        result.Warnings[0].Should().StartWith("The page loaded too slowly");
    }

    [Fact]
    public async Task RunAsync_KeepsTheRequestedAndFinalUrlApartWhenTheProviderFollowedARedirect()
    {
        var (provider, _) = Create(Fixture("manual-and-na.json"));

        var result = await provider.RunAsync(Request);

        result.RequestedUrl.Should().Be("https://example.com/quiet");
        result.FinalUrl.Should().Be("https://www.example.com/quiet");
    }

    [Fact]
    public async Task RunAsync_TreatsALighthouseRuntimeErrorAsAFailedRunNotAZeroScore()
    {
        var (provider, _) = Create(Fixture("runtime-error.json"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.LighthouseRuntimeError);
        failure.Message.Should().Contain("NO_FCP");
    }

    [Fact]
    public async Task RunAsync_TreatsABlockingCaptchaAsItsOwnFailure()
    {
        var (provider, _) = Create(Fixture("captcha-blocked.json"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.CaptchaBlocked);
    }

    [Fact]
    public async Task RunAsync_RefusesAResponseWithNoSeoCategory()
    {
        var (provider, _) = Create(Fixture("missing-seo-category.json"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderContractInvalid);
    }

    /// <summary>
    /// A referenced audit that is absent is a broken contract, not something to skip: dropping it
    /// would leave the score with a gap nothing explains.
    /// </summary>
    [Fact]
    public async Task RunAsync_RefusesAResponseMissingAnAuditTheCategoryReferences()
    {
        var (provider, _) = Create(Fixture("missing-referenced-audit.json"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderContractInvalid);
        failure.Message.Should().Contain("meta-description");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PageAuditFailureCategories.TargetRejected)]
    [InlineData(HttpStatusCode.Unauthorized, PageAuditFailureCategories.ProviderAuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, PageAuditFailureCategories.ProviderAuthenticationFailed)]
    [InlineData(HttpStatusCode.TooManyRequests, PageAuditFailureCategories.ProviderRateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, PageAuditFailureCategories.ProviderUnavailable)]
    [InlineData(HttpStatusCode.BadGateway, PageAuditFailureCategories.ProviderUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PageAuditFailureCategories.ProviderUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, PageAuditFailureCategories.ProviderTimeout)]
    [InlineData(HttpStatusCode.NotFound, PageAuditFailureCategories.UnknownProviderFailure)]
    public async Task RunAsync_NormalizesEveryHttpFailureIntoItsOwnCategory(
        HttpStatusCode status,
        string expectedCategory)
    {
        var (provider, _) = Create(new FakeHandler(status, "{\"error\":{\"message\":\"nope\"}}"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(expectedCategory);
    }

    [Fact]
    public async Task RunAsync_HonoursAUsableRetryAfter()
    {
        var handler = new FakeHandler(HttpStatusCode.TooManyRequests, "{}")
        {
            RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45))
        };
        var (provider, _) = Create(handler);

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
    }

    /// <summary>
    /// An hour-long Retry-After would occupy the single audit worker doing nothing, so it is
    /// ignored rather than obeyed and the ordinary backoff applies instead.
    /// </summary>
    [Fact]
    public async Task RunAsync_IgnoresARetryAfterTooLongToWaitOn()
    {
        var handler = new FakeHandler(HttpStatusCode.TooManyRequests, "{}")
        {
            RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2))
        };
        var (provider, _) = Create(handler);

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_RefusesMalformedJson()
    {
        var (provider, _) = Create(new FakeHandler(HttpStatusCode.OK, "{not json at all"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderResponseInvalid);
    }

    [Fact]
    public async Task RunAsync_RefusesABodyOverTheConfiguredCeiling()
    {
        var oversized = "{\"padding\":\"" + new string('x', 400_000) + "\"}";
        var (provider, _) = Create(
            new FakeHandler(HttpStatusCode.OK, oversized),
            new PageSpeedInsightsOptions { ApiKey = ApiKey, MaximumResponseBytes = 300 * 1024 });

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderResponseTooLarge);
    }

    [Fact]
    public async Task RunAsync_RefusesACategoryDeclaringMoreAuditsThanTheCeilingAllows()
    {
        var (provider, _) = Create(
            Fixture("success-seo-mobile.json"),
            new PageSpeedInsightsOptions { ApiKey = ApiKey, MaximumAuditCount = 3 });

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderResponseTooLarge);
    }

    [Fact]
    public async Task RunAsync_SeparatesACancellationFromATimeout()
    {
        var (provider, _) = Create(new BlockingHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request, cancellation.Token));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.Cancelled);
    }

    [Fact]
    public async Task RunAsync_ReportsAProviderThatNeverAnswersAsATimeout()
    {
        var (provider, _) = Create(
            new BlockingHandler(),
            new PageSpeedInsightsOptions { ApiKey = ApiKey, RequestTimeout = TimeSpan.FromMilliseconds(150) });

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderTimeout);
    }

    [Fact]
    public async Task RunAsync_RefusesToCallTheProviderWithNoApiKeyConfigured()
    {
        var (provider, handler) = Create(
            Fixture("success-seo-mobile.json"),
            new PageSpeedInsightsOptions { ApiKey = null });

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderAuthenticationFailed);
        handler.Requests.Should().BeEmpty("an anonymous call would spend somebody else's quota");
    }

    /// <summary>
    /// The request asks for the SEO category, a strategy and a locale explicitly. Omitting the
    /// category would silently audit Performance, which is this API's default.
    /// </summary>
    [Fact]
    public async Task RunAsync_AsksForTheSeoCategoryStrategyAndLocaleExplicitly()
    {
        var (provider, handler) = Create(Fixture("success-seo-mobile.json"));

        await provider.RunAsync(Request);

        var query = handler.Requests.Single().RequestUri!.Query;
        query.Should().Contain("category=seo");
        query.Should().Contain("strategy=mobile");
        query.Should().Contain("locale=en-US");
    }

    [Fact]
    public async Task RunAsync_SendsTheDesktopStrategyWhenThatIsWhatTheRunRecorded()
    {
        var (provider, handler) = Create(Fixture("success-seo-mobile.json"));

        await provider.RunAsync(Request with { Strategy = PageAuditStrategies.Desktop });

        handler.Requests.Single().RequestUri!.Query.Should().Contain("strategy=desktop");
    }

    /// <summary>
    /// Encoded exactly once. Concatenating would let a query string in the target truncate ours,
    /// and the parameter it would most easily displace is the category.
    /// </summary>
    [Fact]
    public async Task RunAsync_EscapesATargetUrlCarryingItsOwnQueryExactlyOnce()
    {
        var (provider, handler) = Create(Fixture("success-seo-mobile.json"));

        await provider.RunAsync(Request with
        {
            TargetUrl = new Uri("https://example.com/search?q=a&category=performance")
        });

        var uri = handler.Requests.Single().RequestUri!;
        uri.Query.Should().Contain("category=seo");
        System.Web.HttpUtility.ParseQueryString(uri.Query)["url"]
            .Should().Be("https://example.com/search?q=a&category=performance",
                "the target's own query must survive intact and must not become our parameters");
        System.Web.HttpUtility.ParseQueryString(uri.Query).GetValues("category")
            .Should().ContainSingle("the target's category value must not arrive as a second one");
    }

    [Fact]
    public async Task RunAsync_SendsTheRequestToTheOfficialServiceEndpoint()
    {
        var (provider, handler) = Create(Fixture("success-seo-mobile.json"));

        await provider.RunAsync(Request);

        var uri = handler.Requests.Single().RequestUri!;
        uri.Host.Should().Be("pagespeedonline.googleapis.com");
        uri.Scheme.Should().Be("https");
        uri.AbsolutePath.Should().Be("/pagespeedonline/v5/runPagespeed");
    }

    /// <summary>
    /// The key travels in the query string because this API accepts it no other way. Everything
    /// else follows from that: it must never reach a log, an exception or a diagnostic.
    /// </summary>
    [Fact]
    public async Task RunAsync_NeverWritesTheApiKeyOrTheRequestUriToTheLog()
    {
        var recorder = new RecordingLogger();
        var (provider, _) = Create(Fixture("success-seo-mobile.json"), logger: recorder);

        await provider.RunAsync(Request);

        recorder.Lines.Should().NotBeEmpty("the successful run is logged at all");
        recorder.Lines.Should().NotContain(line => line.Contains(ApiKey, StringComparison.Ordinal));
        recorder.Lines.Should().NotContain(line => line.Contains("key=", StringComparison.Ordinal));
        recorder.Lines.Should().NotContain(line =>
            line.Contains("pagespeedonline.googleapis.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_KeepsTheApiKeyOutOfEveryFailureItRaises()
    {
        var (provider, _) = Create(new FakeHandler(HttpStatusCode.Forbidden, $"key {ApiKey} rejected"));

        var failure = await Assert.ThrowsAsync<PageAuditProviderException>(
            () => provider.RunAsync(Request));

        failure.Message.Should().NotContain(ApiKey);
        failure.ToString().Should().NotContain(ApiKey,
            "the provider's own error body is never read back into a diagnostic");
    }

    private static string Mode(PageAuditProviderResult result, string auditId) =>
        result.Items.Single(item => item.AuditId == auditId).ScoreDisplayMode!;

    private static FakeHandler Fixture(string name) => new(
        HttpStatusCode.OK,
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PageSpeed", name)));

    private static (PageSpeedInsightsProvider Provider, FakeHandler Handler) Create(
        BlockingHandler handler,
        PageSpeedInsightsOptions? options = null) =>
        (Build(handler, options, null), new FakeHandler(HttpStatusCode.OK, "{}"));

    private static (PageSpeedInsightsProvider Provider, FakeHandler Handler) Create(
        FakeHandler handler,
        PageSpeedInsightsOptions? options = null,
        ILogger<PageSpeedInsightsProvider>? logger = null) =>
        (Build(handler, options, logger), handler);

    private static PageSpeedInsightsProvider Build(
        HttpMessageHandler handler,
        PageSpeedInsightsOptions? options,
        ILogger<PageSpeedInsightsProvider>? logger)
    {
        options ??= new PageSpeedInsightsOptions { ApiKey = ApiKey };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(PageSpeedInsightsProvider.ServiceOrigin),
            Timeout = Timeout.InfiniteTimeSpan
        };
        return new PageSpeedInsightsProvider(
            new SingleClientFactory(client),
            options,
            logger ?? NullLogger<PageSpeedInsightsProvider>.Instance);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public RetryConditionHeaderValue? RetryAfter { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (RetryAfter is not null)
            {
                response.Headers.RetryAfter = RetryAfter;
            }

            return Task.FromResult(response);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class RecordingLogger : ILogger<PageSpeedInsightsProvider>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The formatted message and the structured values both, because a property carrying
            // the key would reach a log sink even when the message template does not name it.
            Lines.Add(formatter(state, exception));
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                Lines.AddRange(values.Select(value => $"{value.Key}={value.Value}"));
            }

            if (exception is not null)
            {
                Lines.Add(exception.ToString());
            }
        }
    }
}

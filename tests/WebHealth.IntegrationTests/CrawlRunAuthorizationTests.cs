using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class CrawlRunAuthorizationTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    private static readonly Guid Endpoint = Guid.Parse("6f1c9a20-0000-0000-0000-000000000001");

    public static TheoryData<string> EveryRole =>
    [
        ApplicationRoles.Administrator,
        ApplicationRoles.Operations,
        ApplicationRoles.DeveloperSupport,
        ApplicationRoles.Viewer
    ];

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task BrokenLinks_AreReadableByEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/Crawl");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "crawl results are a read surface for every persona that may read the registry");
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task LiveStatus_IsReadableByEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync(
            $"/Crawl/Runs/{EmptyCrawlReportReader.RunningRunId}/Status");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "live crawl progress is part of the registry read surface");
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task LiveResults_AreReadableByEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync(
            $"/Crawl/Runs/{EmptyCrawlReportReader.RunningRunId}/Results");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "incremental crawl results are part of the registry read surface");
    }

    [Fact]
    public async Task RunNow_IsRefusedToAViewer()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.Requested.Clear();

        var response = await PostRunNowAsync(client, Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a Viewer may read every crawl and start none");
        runner.Requested.Should().BeEmpty("the refusal happens before any crawl is opened");
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator)]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    public async Task RunNow_IsAllowedToEveryRoleThatMayTestTargets(string role)
    {
        using var client = factory.CreateHttpsClient(role);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.Requested.Clear();

        var response = await PostRunNowAsync(client, Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/Crawl",
            "the action redirects back to the page showing the crawl it opened");
        runner.Requested.Should().ContainSingle().Which.Should().Be(Endpoint);
    }

    [Fact]
    public async Task RunNow_PassesTheExternalLinkChoiceToTheRunner()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.Requested.Clear();

        await PostRunNowAsync(client, Endpoint, checkExternalLinks: true);

        runner.CheckExternalLinks.Should().BeTrue();
    }

    [Fact]
    public async Task RunNow_IsRefusedWithoutAnAntiForgeryToken()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.Requested.Clear();

        var response = await client.PostAsync(
            "/Crawl/RunNow",
            new FormUrlEncodedContent([new("endpointId", Endpoint.ToString())]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a state-changing post that any page could forge is not protected at all");
        runner.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task RunNow_IsNotReachableByGet()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.Requested.Clear();

        var response = await client.GetAsync($"/Crawl/RunNow?endpointId={Endpoint}");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        runner.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCrawlButton_IsHiddenWhenCrawlsCannotRunOnThisInstance()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.CanQueue = false;
        try
        {
            var html = await client.GetStringAsync($"/Crawl?endpointId={Endpoint}");

            html.Should().NotContain("Run crawl",
                "a control that could only be refused should not be offered");
        }
        finally
        {
            runner.CanQueue = true;
        }
    }

    [Fact]
    public async Task RunCrawlButton_IsOfferedWhenCrawlsCanRun()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var runner = factory.Services.GetRequiredService<RecordingCrawlRunner>();
        runner.CanQueue = true;

        var html = await client.GetStringAsync($"/Crawl?endpointId={Endpoint}");

        html.Should().Contain("Run crawl",
            "the button is the point of the feature, so its absence must fail loudly");
    }

    [Fact]
    public async Task AnonymousRequest_IsSentToLogin()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/Crawl");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Account/Login");
    }

    private static async Task<HttpResponseMessage> PostRunNowAsync(
        HttpClient client,
        Guid endpointId,
        bool checkExternalLinks = false)
    {
        using var page = await client.GetAsync($"/Crawl?endpointId={endpointId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Crawl/RunNow")
        {
            Content = new FormUrlEncodedContent(
            [
                new("endpointId", endpointId.ToString()),
                new("checkExternalLinks", checkExternalLinks.ToString()),
                new("__RequestVerificationToken", token)
            ])
        };

        return await client.SendAsync(request);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        const string Marker = "name=\"__RequestVerificationToken\"";
        var marker = html.IndexOf(Marker, StringComparison.Ordinal);
        marker.Should().BeGreaterThan(-1, "the page must render a form carrying the token");

        const string ValueMarker = "value=\"";
        var start = html.IndexOf(ValueMarker, marker, StringComparison.Ordinal) + ValueMarker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }
}

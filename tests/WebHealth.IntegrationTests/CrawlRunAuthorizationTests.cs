using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Who may read the Broken links page and who may start a crawl, made against the real routes.
/// Hiding the Run crawl button is a usability choice; only the request itself proves the server
/// refuses.
/// <para>
/// This surface deserves the scrutiny: a crawl fetches a whole site this application does not own,
/// and <c>CrawlController.RunNow</c> is the only trigger a crawl has anywhere in the system.
/// </para>
/// </summary>
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

    /// <summary>
    /// Fetching a site page by page is active testing of that target, so it needs the same
    /// permission a manual check needs. A Viewer may read every crawl and start none.
    /// </summary>
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

    /// <summary>
    /// A crawl must not be startable by following a link, which is what a GET route would make it.
    /// </summary>
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

    /// <summary>
    /// With crawl scheduling off there is no worker serving the crawl queue. Offering the button
    /// there would open a run nothing ever picks up, and that run would hold the endpoint's only
    /// active-crawl slot until the staleness window expires.
    /// </summary>
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

    /// <summary>
    /// Posts with a valid anti-forgery token, taken from the page that hosts the form. Requesting
    /// the page first is what a browser does, and it is the only way to obtain the pair of tokens
    /// the framework validates.
    /// </summary>
    private static async Task<HttpResponseMessage> PostRunNowAsync(HttpClient client, Guid endpointId)
    {
        using var page = await client.GetAsync($"/Crawl?endpointId={endpointId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Crawl/RunNow")
        {
            Content = new FormUrlEncodedContent(
            [
                new("endpointId", endpointId.ToString()),
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

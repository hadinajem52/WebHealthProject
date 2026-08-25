using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PageAuditAuthorizationTests(WebHealthWebApplicationFactory factory)
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
    public async Task PageAudits_AreReadableByEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/PageAudits");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a Lighthouse score is a read surface for every persona that may read the registry");
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task SelectingAnEndpoint_RendersItsScore(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync($"/PageAudits?endpointId={Endpoint}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdministratorSeesEndpointSettingsAsAnIconOnlyAction()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.GetAsync($"/PageAudits?endpointId={Endpoint}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain($"/PageAuditSettings?endpointId={Endpoint}");
        html.Should().Contain("button button--secondary button--icon");
        html.Should().NotContain("<span>Incident settings</span>");
    }

    [Theory]
    [InlineData("/PageAudits")]
    [InlineData("/PageAudits?endpointId=6f1c9a20-0000-0000-0000-000000000001")]
    public async Task AnonymousRequest_IsSentToLogin(string path)
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task UnparseableEndpointId_ShowsThePickerRatherThanFailing()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync("/PageAudits?endpointId=not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RunNow_IsRefusedToAViewer()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        var runner = factory.Services.GetRequiredService<RecordingPageAuditRunner>();
        runner.Requested.Clear();

        var response = await PostRunNowAsync(client, Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a Viewer may read every score and start none");
        runner.Requested.Should().BeEmpty("the refusal happens before any run is opened");
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator)]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    public async Task RunNow_IsAllowedToEveryRoleThatMayTestTargets(string role)
    {
        using var client = factory.CreateHttpsClient(role);
        var runner = factory.Services.GetRequiredService<RecordingPageAuditRunner>();
        runner.Requested.Clear();

        var response = await PostRunNowAsync(client, Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/PageAudits",
            "the action redirects back to the page showing the run it opened");
        runner.Requested.Should().ContainSingle().Which.Should().Be(Endpoint);
    }

    [Fact]
    public async Task ARunIdThatResolvesToNothing_IsNotFound()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync(
            $"/PageAudits?endpointId={Endpoint}&runId={Guid.Empty}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RunNow_IsRefusedWithoutAnAntiForgeryToken()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.PostAsync(
            "/PageAudits/RunNow",
            new FormUrlEncodedContent([new("endpointId", Endpoint.ToString())]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a state-changing post that any page could forge is not protected at all");
    }

    [Fact]
    public async Task RunNow_IsNotReachableByGet()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.GetAsync($"/PageAudits/RunNow?endpointId={Endpoint}");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    private static async Task<HttpResponseMessage> PostRunNowAsync(HttpClient client, Guid endpointId)
    {
        using var page = await client.GetAsync($"/PageAudits?endpointId={endpointId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/PageAudits/RunNow")
        {
            Content = new FormUrlEncodedContent(
            [
                new("endpointId", endpointId.ToString()),
                new("__RequestVerificationToken", token)
            ])
        };

        foreach (var cookie in page.Headers.GetValues("Set-Cookie"))
        {
            request.Headers.Add("Cookie", cookie.Split(';')[0]);
        }

        return await client.SendAsync(request);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\"";
        var nameIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
        {
            return string.Empty;
        }

        const string valueMarker = "value=\"";
        var valueIndex = html.IndexOf(valueMarker, nameIndex, StringComparison.Ordinal);
        var start = valueIndex + valueMarker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }
}

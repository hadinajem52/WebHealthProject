using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Ajax;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngAuditAuthorizationTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    private static readonly Guid EndpointId = EmptyTargetRegistryReader.Endpoint.Id;

    public static TheoryData<string> EveryRole =>
    [
        ApplicationRoles.Administrator,
        ApplicationRoles.Operations,
        ApplicationRoles.DeveloperSupport,
        ApplicationRoles.Viewer
    ];

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task PngAudit_IsReadableByEveryRegistryPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/Tools/PngImages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousRequest_IsSentToLogin()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/Tools/PngImages");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task Sidebar_ExposesTheExplicitPngAuditRoute()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync("/Tools/PngImages");

        html.Should().Contain(">Tools<");
        html.Should().Contain("href=\"/Tools/PngImages\"");
        html.Should().Contain(">PNG image audit<");
    }

    [Fact]
    public async Task ViewerCannotQueueAnAudit()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        var runner = factory.Services.GetRequiredService<RecordingPngAuditRunner>();
        runner.Requested.Clear();

        var response = await PostRunAsync(client, EndpointId, true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        runner.Requested.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator)]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    public async Task TestingRolesReceiveAcceptedForAjaxRuns(string role)
    {
        using var client = factory.CreateHttpsClient(role);
        var runner = factory.Services.GetRequiredService<RecordingPngAuditRunner>();
        runner.Requested.Clear();

        var response = await PostRunAsync(client, EndpointId, true);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        payload.Should().Contain("/Tools/PngImages/Status");
        payload.Should().Contain(EmptyPngAuditReader.RunningRunId.ToString());
        runner.Requested.Should().ContainSingle().Which.Should().Be(EndpointId);
    }

    [Fact]
    public async Task RunPostRequiresAnAntiForgeryToken()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.PostAsync(
            "/Tools/PngImages/Run",
            new FormUrlEncodedContent([new("endpointId", EndpointId.ToString())]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ActiveStatusUsesAcceptedAndDeclaresTheExistingPollerContract()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync(
            $"/Tools/PngImages/Status?endpointId={EndpointId}&runId={EmptyPngAuditReader.RunningRunId}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        html.Should().Contain("id=\"png-audit-results\"");
        html.Should().Contain("data-run-active=\"true\"");
        html.Should().Contain("data-run-also=\"#png-audit-run-action\"");
    }

    [Fact]
    public async Task TerminalRunUsesOkAndRendersTheRequiredResultContract()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        var path = $"/Tools/PngImages/Status?endpointId={EndpointId}"
            + $"&runId={EmptyPngAuditReader.CompletedRunId}&details=true&filter=webp-candidates";

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Pages inspected");
        html.Should().Contain("Image-analysis coverage");
        html.Should().Contain("Lossless WebP size");
        html.Should().Contain("Potential saving");
        html.Should().Contain("lossless WebP candidate");
        html.Should().Contain("token=REDACTED");
        html.Should().NotContain("src=\"https://example.com");
    }

    [Fact]
    public async Task UnknownRunIsNotFound()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync($"/Tools/PngImages/Runs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> PostRunAsync(
        HttpClient client,
        Guid endpointId,
        bool ajax)
    {
        using var page = await client.GetAsync($"/Tools/PngImages?endpointId={endpointId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Tools/PngImages/Run")
        {
            Content = new FormUrlEncodedContent(
            [
                new("endpointId", endpointId.ToString()),
                new("__RequestVerificationToken", token)
            ])
        };
        if (ajax)
        {
            request.Headers.Add(AjaxResponseHeaders.Request, "1");
        }

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

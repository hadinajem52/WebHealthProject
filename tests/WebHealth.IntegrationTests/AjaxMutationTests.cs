using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Ajax;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class AjaxMutationTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task ManualCheckAjaxRequestReturnsQueuedRunContract()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            $"/Checks/RunCheck?id={EmptyTargetRegistryReader.Endpoint.Id}",
            token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(EmptyManualCheckService.LogicalCheckId, json.RootElement.GetProperty("runId").GetGuid());
        Assert.Contains("/Checks/Status", json.RootElement.GetProperty("statusUrl").GetString(), StringComparison.Ordinal);
        Assert.Contains("queued", json.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualCheckWithoutAjaxKeepsRedirectFallback()
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(ApplicationRoles.Administrator);
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            $"/Checks/RunCheck?id={EmptyTargetRegistryReader.Endpoint.Id}",
            token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Targets/Endpoint", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndpointLifecycleAjaxRequestReturnsAuthoritativeRefreshAddress()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            "/Targets/PauseEndpointSchedule",
            token,
            ("id", EmptyTargetRegistryReader.Endpoint.Id.ToString()),
            ("version", "1"));
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Targets/Endpoint", json.RootElement.GetProperty("refreshUrl").GetString(), StringComparison.Ordinal);
        Assert.Contains("paused", json.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncidentLifecycleAjaxRequestReturnsWorkspaceRefreshAddress()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);
        var incidentId = Guid.NewGuid();

        using var response = await PostAsync(
            client,
            "/Incidents/Acknowledge",
            token,
            ("id", incidentId.ToString()),
            ("version", "1"));
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"/Incidents/Details/{incidentId}", json.RootElement.GetProperty("refreshUrl").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarkNotificationsReadAjaxRequestRefreshesOnlyTheMenu()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, "/Notifications/MarkRead", token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Notifications/Menu", json.RootElement.GetProperty("refreshUrl").GetString());
    }

    [Fact]
    public async Task AjaxMutationWithoutAntiforgeryTokenIsRejected()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.PostAsync(
            $"/Checks/RunCheck?id={EmptyTargetRegistryReader.Endpoint.Id}",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PageSpeedAjaxRequestReturnsQueuedRunAndStatusAddress()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            "/PageAudits/RunNow",
            token,
            ("endpointId", EmptyTargetRegistryReader.Endpoint.Id.ToString()),
            ("strategy", "mobile"));
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(EmptyPageAuditReader.QueuedRunId, json.RootElement.GetProperty("runId").GetGuid());
        Assert.Contains("/PageAudits/Status", json.RootElement.GetProperty("statusUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageSpeedStatusUsesAcceptedUntilTheRunIsTerminal()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var endpointId = EmptyTargetRegistryReader.Endpoint.Id;

        using var active = await client.GetAsync(
            $"/PageAudits/Status?endpointId={endpointId}&strategy=mobile&runId={EmptyPageAuditReader.QueuedRunId}");
        using var completed = await client.GetAsync(
            $"/PageAudits/Status?endpointId={endpointId}&strategy=mobile&runId={EmptyPageAuditReader.CompletedRunId}");
        var completedContent = await completed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, active.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Contains("id=\"ajax-page\"", completedContent, StringComparison.Ordinal);
        Assert.Contains("data-page-audit-active=\"false\"", completedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE html>", completedContent, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var content = await client.GetStringAsync("/Targets/Endpoints");
        var match = Regex.Match(
            content,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string token,
        params (string Name, string Value)[] values)
    {
        var fields = values.ToDictionary(value => value.Name, value => value.Value);
        fields["__RequestVerificationToken"] = token;
        return client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}

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
        var statusUrl = json.RootElement.GetProperty("statusUrl").GetString();
        Assert.Contains("/Checks/Status", statusUrl, StringComparison.Ordinal);

        using var statusResponse = await client.GetAsync(statusUrl);
        using var statusJson = await ReadJsonAsync(statusResponse);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Contains("/Targets/Endpoint", statusJson.RootElement.GetProperty("refreshUrl").GetString(), StringComparison.Ordinal);
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
    public async Task ArchiveResolvedAjaxRequestRefreshesTheIncidentList()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, "/Incidents/ArchiveResolved", token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Incidents", json.RootElement.GetProperty("refreshUrl").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("archive", json.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreAjaxRequestRefreshesTheIncidentArchive()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            "/Incidents/Restore",
            token,
            ("id", Guid.NewGuid().ToString()),
            ("version", "1"));
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Incidents/Archived", json.RootElement.GetProperty("refreshUrl").GetString(), StringComparison.OrdinalIgnoreCase);
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
        var refreshUrl = json.RootElement.GetProperty("refreshUrl").GetString();
        Assert.Equal("/Notifications/Menu", refreshUrl);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("message").ValueKind);

        using var menu = await client.GetAsync(refreshUrl);

        Assert.Equal(HttpStatusCode.OK, menu.StatusCode);
        Assert.True(menu.Headers.CacheControl?.NoStore == true);
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
        var token = await GetAntiforgeryTokenAsync(
            client,
            $"/PageAudits?endpointId={EmptyTargetRegistryReader.Endpoint.Id}");

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
    public async Task PageSpeedStatusStaysAcceptedWhileAnotherCategoryIsActive()
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
        Assert.Equal(HttpStatusCode.Accepted, completed.StatusCode);
        Assert.Contains("id=\"ajax-page\"", completedContent, StringComparison.Ordinal);
        Assert.Contains("id=\"page-audit-results\"", completedContent, StringComparison.Ordinal);
        Assert.Contains("data-run-active=\"true\"", completedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE html>", completedContent, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Registry/CreateClient")]
    [InlineData("/Registry/EditClient")]
    [InlineData("/Registry/CreateWebsite")]
    [InlineData("/Registry/EditWebsite")]
    [InlineData("/Targets/CreateEnvironment")]
    [InlineData("/Targets/EditEnvironment")]
    [InlineData("/Maintenance/Create")]
    [InlineData("/Maintenance/Edit")]
    [InlineData("/Targets/CreateEndpoint")]
    [InlineData("/Targets/EditEndpoint")]
    [InlineData("/Administration/CreateTeam")]
    [InlineData("/Administration/EditTeam")]
    [InlineData("/Administration/CreateUser")]
    [InlineData("/Administration/EditUser")]
    public async Task CrudFormValidationReturnsReplaceableFragment(string path)
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, path, token);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("id=\"ajax-form-region\"", content, StringComparison.Ordinal);
        Assert.Contains("data-shell-validation-summary", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrudFormValidationKeepsNormalFullPageFallback()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, "/Registry/CreateClient", token);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-shell-validation-summary", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrudFormSuccessReturnsExplicitLocalNavigation()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            "/Registry/CreateClient",
            token,
            ("Name", "AJAX client"),
            ("OwnerSubjectId", Guid.NewGuid().ToString()));
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("/Registry/Client/", json.RootElement.GetProperty("redirectUrl").GetString(), StringComparison.Ordinal);
        Assert.Contains("created", json.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrudNavigationPreservesTheSuccessMessageOnTheDestinationPage()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            "/Administration/CreateUser",
            token,
            ("DisplayName", "AJAX user"),
            ("Email", "ajax-user@example.test"),
            ("Password", "Testing1234!"),
            ("Roles", ApplicationRoles.Viewer));
        using var json = await ReadJsonAsync(response);
        var redirectUrl = json.RootElement.GetProperty("redirectUrl").GetString();
        client.DefaultRequestHeaders.Remove(AjaxResponseHeaders.Request);

        var destination = await client.GetStringAsync(redirectUrl!);

        Assert.Contains("User created successfully.", destination, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", destination, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleEditReturnsConflictFragmentWithSubmittedValues()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);
        var submittedName = "Preserved stale client";

        using var response = await PostAsync(
            client,
            "/Registry/EditClient",
            token,
            ("ClientId", Guid.NewGuid().ToString()),
            ("Version", "-1"),
            ("Name", submittedName),
            ("OwnerSubjectId", Guid.NewGuid().ToString()));
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(submittedName, content, StringComparison.Ordinal);
        Assert.Contains("value=\"-1\"", content, StringComparison.Ordinal);
        Assert.Contains("changed after you opened it", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path = "/Targets/Endpoints")
    {
        var ajaxValues = client.DefaultRequestHeaders
            .TryGetValues(AjaxResponseHeaders.Request, out var values)
            ? values.ToArray()
            : null;
        client.DefaultRequestHeaders.Remove(AjaxResponseHeaders.Request);

        try
        {
            var content = await client.GetStringAsync(path);
            var match = Regex.Match(
                content,
                "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
                RegexOptions.CultureInvariant);
            Assert.True(match.Success, content);
            return WebUtility.HtmlDecode(match.Groups["token"].Value);
        }
        finally
        {
            if (ajaxValues is not null)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    AjaxResponseHeaders.Request,
                    ajaxValues);
            }
        }
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

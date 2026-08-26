using System.Net;
using System.Text.RegularExpressions;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Ajax;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class EndpointFirstRegistryTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task RegistryNavigationTargetsEndpointsAndRemainsCurrentOnRegistryPages()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var endpointInventory = await client.GetStringAsync("/Targets/Endpoints");
        var website = await client.GetStringAsync($"/Registry/Website/{EmptyRegistryReader.Website.Id}");

        Assert.Matches(
            "<a(?=[^>]*class=\"app-nav__link is-current\")(?=[^>]*href=\"/Targets/Endpoints\")(?=[^>]*aria-current=\"page\")[^>]*>",
            endpointInventory);
        Assert.Matches(
            "<a(?=[^>]*class=\"app-nav__link is-current\")(?=[^>]*href=\"/Targets/Endpoints\")(?=[^>]*aria-current=\"page\")[^>]*>",
            website);
        Assert.Contains("href=\"/Targets/Endpoints\">Registry</a>", website, StringComparison.Ordinal);
        Assert.Contains("href=\"/Registry/Clients\"", endpointInventory, StringComparison.Ordinal);
        Assert.Contains("href=\"/Registry/Websites\"", endpointInventory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagingDetailPagesOfferRegistrationWithKnownHierarchyPrefilled()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var clientPage = await client.GetStringAsync($"/Registry/Client/{EmptyRegistryReader.Client.Id}");
        var websitePage = await client.GetStringAsync($"/Registry/Website/{EmptyRegistryReader.Website.Id}");
        var environmentPage = await client.GetStringAsync(
            $"/Targets/Environment/{EmptyTargetRegistryReader.Environment.Id}");

        Assert.Contains(
            $"/Targets/RegisterEndpoint?clientId={EmptyRegistryReader.Client.Id}",
            clientPage,
            StringComparison.Ordinal);
        Assert.Contains($"clientId={EmptyRegistryReader.Client.Id}", websitePage, StringComparison.Ordinal);
        Assert.Contains($"websiteId={EmptyRegistryReader.Website.Id}", websitePage, StringComparison.Ordinal);
        Assert.Contains($"clientId={EmptyRegistryReader.Client.Id}", environmentPage, StringComparison.Ordinal);
        Assert.Contains($"websiteId={EmptyRegistryReader.Website.Id}", environmentPage, StringComparison.Ordinal);
        Assert.Contains($"environmentId={EmptyTargetRegistryReader.Environment.Id}", environmentPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonManagingDetailPagesDoNotOfferRegistration()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var clientPage = await client.GetStringAsync($"/Registry/Client/{EmptyRegistryReader.Client.Id}");
        var websitePage = await client.GetStringAsync($"/Registry/Website/{EmptyRegistryReader.Website.Id}");
        var environmentPage = await client.GetStringAsync(
            $"/Targets/Environment/{EmptyTargetRegistryReader.Environment.Id}");

        Assert.DoesNotContain("Register endpoint", clientPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Register endpoint", websitePage, StringComparison.Ordinal);
        Assert.DoesNotContain("Register endpoint", environmentPage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clientId")]
    [InlineData("websiteId")]
    [InlineData("environmentId")]
    public async Task EveryPrefillRouteIsProtectedFromNonManagingRoles(string parameter)
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(ApplicationRoles.Viewer);
        var id = parameter switch
        {
            "clientId" => EmptyRegistryReader.Client.Id,
            "websiteId" => EmptyRegistryReader.Website.Id,
            _ => EmptyTargetRegistryReader.Environment.Id
        };

        using var response = await client.GetAsync($"/Targets/RegisterEndpoint?{parameter}={id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("clientId")]
    [InlineData("websiteId")]
    [InlineData("environmentId")]
    public async Task EveryContextPrefillSelectsItsLevelAndEveryLevelAboveIt(string parameter)
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        var id = parameter switch
        {
            "clientId" => EmptyRegistryReader.Client.Id,
            "websiteId" => EmptyRegistryReader.Website.Id,
            _ => EmptyTargetRegistryReader.Environment.Id
        };

        var content = await client.GetStringAsync($"/Targets/RegisterEndpoint?{parameter}={id}");

        AssertSelected(content, id.ToString());
        AssertSelected(content, EmptyRegistryReader.Client.Id.ToString());
        if (parameter != "clientId")
        {
            AssertSelected(content, EmptyRegistryReader.Website.Id.ToString());
        }

        Assert.Matches("<input(?=[^>]*name=\"Url\")(?=[^>]*autofocus)[^>]*>", content);
    }

    [Fact]
    public async Task EndpointFiltersSurviveAnAjaxRoundTrip()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var endpoint = EmptyTargetRegistryReader.Endpoint;
        var query = $"search=example&clientId={endpoint.ClientId}&websiteId={endpoint.WebsiteId}"
            + $"&environmentId={endpoint.EnvironmentId}";

        using var response = await client.GetAsync($"/Targets/Endpoints?{query}");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"ajax-page\"", content, StringComparison.Ordinal);
        Assert.Contains("value=\"example\"", content, StringComparison.Ordinal);
        AssertSelected(content, endpoint.ClientId.ToString());
        AssertSelected(content, endpoint.WebsiteId.ToString());
        AssertSelected(content, endpoint.EnvironmentId.ToString());
        Assert.Contains(endpoint.DisplayUrl, content, StringComparison.Ordinal);
        Assert.Contains("Clear", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("search=missing")]
    [InlineData("clientId=11111111-1111-1111-1111-111111111111")]
    [InlineData("websiteId=11111111-1111-1111-1111-111111111111")]
    [InlineData("environmentId=11111111-1111-1111-1111-111111111111")]
    public async Task EachEndpointFilterCanNarrowTheInventory(string query)
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync($"/Targets/Endpoints?{query}");

        Assert.Contains("No endpoints found", content, StringComparison.Ordinal);
        Assert.DoesNotContain(EmptyTargetRegistryReader.Endpoint.DisplayUrl, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandaloneHierarchyCreationActionsAreSecondary()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var clients = await client.GetStringAsync("/Registry/Clients");
        var websites = await client.GetStringAsync("/Registry/Websites");

        Assert.Matches(
            "<a(?=[^>]*class=\"button button--secondary\")(?=[^>]*href=\"/Registry/CreateClient\")[^>]*>",
            clients);
        Assert.Matches(
            "<a(?=[^>]*class=\"button button--secondary\")(?=[^>]*href=\"/Registry/CreateWebsite\")[^>]*>",
            websites);
    }

    [Fact]
    public async Task EnvironmentUsesTheSharedDetailAndActionMenuPattern()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var content = await client.GetStringAsync(
            $"/Targets/Environment/{EmptyTargetRegistryReader.Environment.Id}");

        Assert.Contains("class=\"card__status\"", content, StringComparison.Ordinal);
        Assert.Contains("class=\"detail-list\"", content, StringComparison.Ordinal);
        Assert.Contains("data-shell-menu", content, StringComparison.Ordinal);
        Assert.Contains("action-menu__item--danger", content, StringComparison.Ordinal);
        Assert.Contains("Environment type", content, StringComparison.Ordinal);
        Assert.Contains("Base URL", content, StringComparison.Ordinal);
        Assert.Contains("Version", content, StringComparison.Ordinal);
        Assert.DoesNotContain("registry-facts", content, StringComparison.Ordinal);
        Assert.DoesNotContain("registry-lifecycle", content, StringComparison.Ordinal);
    }

    private static void AssertSelected(string content, string value) =>
        Assert.Matches(
            $"<option(?=[^>]*value=\"{Regex.Escape(value)}\")(?=[^>]*selected=\"selected\")[^>]*>",
            content);
}

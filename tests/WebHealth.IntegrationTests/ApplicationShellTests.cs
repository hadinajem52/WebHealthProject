using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using WebHealth.IntegrationTests.Support;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Shell;
using WebHealth.Web.Models;
using WebHealth.Application.Registry;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed partial class ApplicationShellTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    /// <summary>
    /// The dashboard is a registry read surface, so it states its policy at the boundary. A
    /// signed-in account with no application role must be denied rather than shown an empty
    /// dashboard, which would hide the authorization decision inside query behaviour.
    /// </summary>
    [Fact]
    public async Task Dashboard_DeniesASignedInUserWithNoApplicationRole()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_RendersTheSharedShellLandmarks()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("""<a class="skip-link" href="#main-content">""", content, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Primary\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Breadcrumb\"", content, StringComparison.Ordinal);
        Assert.Contains("<main", content, StringComparison.Ordinal);
        Assert.Contains("<header", content, StringComparison.Ordinal);
        Assert.Contains("<footer", content, StringComparison.Ordinal);
        Assert.Contains("<h1 class=\"app-title\">Dashboard</h1>", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Navigation_MarksTheCurrentPageAndDoesNotLinkPlannedDestinations()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.Contains("aria-current=\"page\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", content, StringComparison.Ordinal);
        Assert.Contains(">Planned<", content, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_ReferencesOnlyAssetsTheApplicationServes()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");
        var assets = AssetReference()
            .Matches(content)
            .Select(match => match.Groups["url"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(assets);
        foreach (var asset in assets)
        {
            using var assetResponse = await client.GetAsync(asset);
            Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        }
    }

    [Fact]
    public async Task SidebarSupportArtwork_IsServed()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        using var response = await client.GetAsync("/images/sidebar-support.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FlashMessage_IsShownOnceAfterARedirect()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var afterRedirect = await client.GetStringAsync("/__tests/shell/flash");
        var afterReload = await client.GetStringAsync("/");

        Assert.Contains(ShellProbeController.FlashText, afterRedirect, StringComparison.Ordinal);
        Assert.Contains("class=\"flash\"", afterRedirect, StringComparison.Ordinal);
        Assert.DoesNotContain(ShellProbeController.FlashText, afterReload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationSummary_ReportsModelErrorsAboveThePageContent()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/__tests/shell/validation");

        Assert.Contains("class=\"validation-summary\"", content, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", content, StringComparison.Ordinal);
        Assert.Contains(ShellProbeController.ValidationText, content, StringComparison.Ordinal);
        Assert.True(
            content.IndexOf("class=\"validation-summary\"", StringComparison.Ordinal)
            < content.IndexOf("class=\"stack\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyState_DescribesMissingDataWithText()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.Contains("class=\"empty-state\"", content, StringComparison.Ordinal);
        Assert.Contains("No monitors match these filters", content, StringComparison.Ordinal);
        Assert.Contains("No open incidents", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// BR-R01. A dashboard that does not say what it was filtered to, and when it was read,
    /// cannot be compared against another view of the same page.
    /// </summary>
    [Fact]
    public async Task Dashboard_DisclosesTheAppliedFiltersAndTheAsOfInstant()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/?HealthStatus=Critical");

        Assert.Contains("aria-label=\"Applied filters and data freshness\"", content, StringComparison.Ordinal);
        Assert.Contains("<dt>Health status</dt>", content, StringComparison.Ordinal);
        Assert.Contains("<dt>As of</dt>", content, StringComparison.Ordinal);
        Assert.Contains("<dt>Window</dt>", content, StringComparison.Ordinal);
        Assert.Contains("(exclusive)", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_SaysSoWhenNoFilterIsApplied()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.Contains("everything you have access to", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_RejectsAnOutOfBoundsWindowInsteadOfServingIt()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/?WindowStart=2000-01-01&WindowEnd=2030-01-01");

        Assert.Contains("Filter not applied", content, StringComparison.Ordinal);
        Assert.Contains("cannot be longer than", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Phase 2 accessibility box: status must not be carried by colour alone. The shape is
    /// applied to <c>.badge</c> itself, so every badge in the application inherits it rather
    /// than each view having to remember.
    /// </summary>
    [Fact]
    public async Task StatusBadges_CarryANonColourCue()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var stylesheet = await client.GetStringAsync("/css/components.css");

        Assert.Contains(".badge::before", stylesheet, StringComparison.Ordinal);
        Assert.Contains("[data-status=\"success\"]::before", stylesheet, StringComparison.Ordinal);
        Assert.Contains("[data-status=\"warning\"]::before", stylesheet, StringComparison.Ordinal);
        Assert.Contains("[data-status=\"high\"]::before", stylesheet, StringComparison.Ordinal);
        Assert.Contains("[data-status=\"danger\"]::before", stylesheet, StringComparison.Ordinal);
        Assert.Contains("forced-colors: active", stylesheet, StringComparison.Ordinal);
    }

    /// <summary>
    /// The chart is an enhancement. Its numbers are always available as a table, and the
    /// library behind it is served by this application rather than fetched from a CDN.
    /// </summary>
    [Fact]
    public async Task TrendChart_IsVendoredLocallyAndHasATableEquivalent()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.DoesNotContain("cdn.jsdelivr.net", content, StringComparison.Ordinal);
        Assert.DoesNotContain("//cdn.", content, StringComparison.Ordinal);

        using var script = await client.GetAsync("/lib/chartjs/dist/chart.umd.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    [Fact]
    public async Task RegistryLabelsTagsAndNotes_AreHtmlEncoded()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/__tests/shell/encoded-registry");

        Assert.DoesNotContain(ShellProbeController.UntrustedMarkup, content, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorPage_UsesTheSharedShellAndTheErrorStateComponent()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync("/Home/HttpStatusCode?code=404");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("id=\"main-content\"", content, StringComparison.Ordinal);
        Assert.Contains("class=\"error-state\"", content, StringComparison.Ordinal);
        Assert.Contains("Error 404", content, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Breadcrumb\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependencyUnavailable_HasADedicatedStateWithARetryAction()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync("/__tests/shell/unavailable");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Service unavailable", content, StringComparison.Ordinal);
        Assert.Contains("class=\"error-state\"", content, StringComparison.Ordinal);
        Assert.Contains("""<a class="button button--primary" href="/__tests/shell/unavailable">Try again</a>""", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task RetryIsNotOfferedForOtherErrorStates(int statusCode)
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        using var response = await client.GetAsync($"/Home/HttpStatusCode?code={statusCode}");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("class=\"error-state\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Try again", content, StringComparison.Ordinal);
    }

    [GeneratedRegex("(?:href|src)=\"(?<url>/[^\"]+\\.(?:css|js|ico)(?:\\?[^\"]*)?)\"")]
    private static partial Regex AssetReference();
}

[Route("__tests/shell")]
public sealed class ShellProbeController : Controller
{
    public const string FlashText = "Shell flash probe message.";
    public const string ValidationText = "Shell validation probe message.";
    public const string UntrustedMarkup = "<script>alert('encoded')</script>";

    [HttpGet("flash")]
    public IActionResult Flash()
    {
        TempData.AddFlashMessage(FlashLevel.Success, FlashText);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("validation")]
    public IActionResult Validation()
    {
        ModelState.AddModelError("Probe", ValidationText);
        return View("~/Views/Home/Index.cshtml", EmptyDashboard.ViewModel(DateTimeOffset.UtcNow));
    }

    [HttpGet("encoded-registry")]
    public IActionResult EncodedRegistry()
    {
        var website = new WebsiteListItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Client",
            UntrustedMarkup,
            "Owner",
            null,
            false,
            false,
            1,
            0,
            [UntrustedMarkup]);
        var client = new ClientDetails(
            Guid.NewGuid(),
            "Client",
            Guid.NewGuid(),
            "Owner",
            UntrustedMarkup,
            true,
            false,
            1,
            [website]);
        return View(
            "~/Views/Registry/Client.cshtml",
            new ClientDetailsViewModel(client, false));
    }

    [HttpGet("unavailable")]
    public IActionResult Unavailable()
    {
        return StatusCode((int)HttpStatusCode.ServiceUnavailable);
    }
}

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
        Assert.Contains("<main", content, StringComparison.Ordinal);
        Assert.Contains("<header", content, StringComparison.Ordinal);
        Assert.Contains("<footer", content, StringComparison.Ordinal);
        Assert.Contains("<h1 class=\"app-title\">Dashboard</h1>", content, StringComparison.Ordinal);

        // A top-level page has no ancestor to climb to, so its one-item trail — which only
        // repeated the <h1> directly above it — is not rendered. The trail itself is still
        // covered on a page that has one; see ErrorPage_UsesTheSharedShellAndTheErrorStateComponent.
        Assert.DoesNotContain("aria-label=\"Breadcrumb\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The header search accepted text and was wired to nothing — no form, no handler, no
    /// results. A control that invites input it can never answer is a false affordance, so it is
    /// gone until search exists.
    /// </summary>
    [Fact]
    public async Task Header_DoesNotOfferASearchControlThatCannotSearch()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.DoesNotContain("app-search", content, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"search\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dashboard leads with what is broken rather than with how to filter. The order is the
    /// product decision under test: current state and incidents must precede endpoint detail and
    /// the window figures in the served markup, not merely exist somewhere on the page.
    /// </summary>
    /// <remarks>
    /// The headings are located by their rendered <c>h2</c>, not by the bare identifier. The
    /// identifier also appears earlier in the document as the alert banner's fragment link, so
    /// searching for the raw string finds the anchor and reports an order the page does not have.
    /// </remarks>
    [Fact]
    public async Task Dashboard_PlacesCurrentStateAndIncidentsAheadOfEndpointAndWindowDetail()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        var status = HeadingPosition(content, "dashboard-status-heading");
        var incidents = HeadingPosition(content, "dashboard-incidents-heading");
        var health = HeadingPosition(content, "dashboard-health-heading");
        var totals = HeadingPosition(content, "dashboard-totals-heading");

        Assert.True(status > 0, "The current-status section is missing.");
        Assert.True(incidents > status, "Active incidents must follow current status.");
        Assert.True(health > incidents, "Endpoint health must follow active incidents.");
        Assert.True(totals > health, "Window figures must follow endpoint health.");
    }

    /// <summary>
    /// The eight-field filter is reachable from the action bar rather than occupying the first
    /// screen. It is a popup built on the shell's shared menu contract, and — like the header
    /// menus — its form is in the served markup so filtering never depends on JavaScript.
    /// </summary>
    [Fact]
    public async Task Dashboard_OffersTheFilterAsAMenuWhoseFormIsStillServed()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.Contains("data-shell-filters-toggle", content, StringComparison.Ordinal);
        Assert.Contains("data-shell-filters-menu", content, StringComparison.Ordinal);

        // Every dimension the report query supports still has a control.
        foreach (var field in new[]
                 {
                     "ClientId", "WebsiteId", "EnvironmentId", "OwnerSubjectId",
                     "HealthStatus", "MonitorType", "WindowStart", "WindowEnd"
                 })
        {
            Assert.Contains($"name=\"{field}\"", content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The position of a section heading in the served markup, located by its rendered element so
    /// a fragment link carrying the same identifier cannot be mistaken for the section itself.
    /// </summary>
    private static int HeadingPosition(string content, string headingId) =>
        content.IndexOf($"id=\"{headingId}\">", StringComparison.Ordinal);

    /// <summary>
    /// The window figures are a rate over a period and the status chips are the state right now.
    /// They are named for what they are, so a reader is not left reconciling "Uptime 100%" with a
    /// "Critical" badge beside it.
    /// </summary>
    [Fact]
    public async Task Dashboard_NamesWindowFiguresApartFromCurrentState()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.Contains("Current status", content, StringComparison.Ordinal);
        Assert.Contains("Performance over the selected period", content, StringComparison.Ordinal);
        Assert.Contains("Window availability", content, StringComparison.Ordinal);
        Assert.Contains("Warning-free responses", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The chart's accessible table stays in the served HTML. It is behind a disclosure so a
    /// sighted reader is not shown every daily row twice, but a reader without script — or
    /// without the vendored chart library — must still receive the numbers.
    /// </summary>
    [Fact]
    public async Task Dashboard_ServesTheChartDataAsMarkupEvenThoughItIsCollapsed()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        // Either the trend has rows and the disclosure carries them, or the window held no
        // samples and the empty state says so. Both are correct; a silent chart-only page is not.
        var hasTable = content.Contains("chart-table", StringComparison.Ordinal);
        var hasEmptyState = content.Contains("No samples in this window", StringComparison.Ordinal);
        Assert.True(hasTable || hasEmptyState, "The trend data is neither tabulated nor explained.");
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
    /// The Phase 2 accessibility box: status must not be carried by colour alone. The pill no
    /// longer draws a silhouette beside its label — Figma node 1633:352 has none — so the label
    /// itself is the cue, and a pill served with an empty label would leave the fill carrying
    /// the meaning on its own.
    /// </summary>
    [Fact]
    public async Task StatusBadges_NameTheirStatusRatherThanOnlyColouringIt()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        var badges = BadgeLabel().Matches(content);
        Assert.NotEmpty(badges);
        Assert.All(badges, badge => Assert.False(
            string.IsNullOrWhiteSpace(badge.Groups[1].Value),
            "A status pill was served with no label, leaving its fill as the only cue."));
    }

    /// <summary>
    /// A pill's visible text, whether it is written directly into the element or wrapped by the
    /// <c>_StatusBadge</c> partial's label span.
    /// </summary>
    [GeneratedRegex("<span class=\"badge\" data-status=\"[a-z]+\">\\s*(?:<span class=\"badge__label\">)?([^<]*)")]
    private static partial Regex BadgeLabel();

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

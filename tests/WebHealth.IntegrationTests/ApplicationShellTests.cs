using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Shell;
using WebHealth.Web.Models;
using WebHealth.Application.Registry;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed partial class ApplicationShellTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task PlaceholderDashboard_RendersTheSharedShellLandmarks()
    {
        using var client = factory.CreateHttpsClient();

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
        using var client = factory.CreateHttpsClient();

        var content = await client.GetStringAsync("/");

        Assert.Contains("aria-current=\"page\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", content, StringComparison.Ordinal);
        Assert.Contains(">Planned<", content, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_ReferencesOnlyAssetsTheApplicationServes()
    {
        using var client = factory.CreateHttpsClient();

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
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/images/sidebar-support.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FlashMessage_IsShownOnceAfterARedirect()
    {
        using var client = factory.CreateHttpsClient();

        var afterRedirect = await client.GetStringAsync("/__tests/shell/flash");
        var afterReload = await client.GetStringAsync("/");

        Assert.Contains(ShellProbeController.FlashText, afterRedirect, StringComparison.Ordinal);
        Assert.Contains("class=\"flash\"", afterRedirect, StringComparison.Ordinal);
        Assert.DoesNotContain(ShellProbeController.FlashText, afterReload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationSummary_ReportsModelErrorsAboveThePageContent()
    {
        using var client = factory.CreateHttpsClient();

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
        using var client = factory.CreateHttpsClient();

        var content = await client.GetStringAsync("/");

        Assert.Contains("class=\"empty-state\"", content, StringComparison.Ordinal);
        Assert.Contains("No monitored endpoints yet", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistryLabelsTagsAndNotes_AreHtmlEncoded()
    {
        using var client = factory.CreateHttpsClient();

        var content = await client.GetStringAsync("/__tests/shell/encoded-registry");

        Assert.DoesNotContain(ShellProbeController.UntrustedMarkup, content, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorPage_UsesTheSharedShellAndTheErrorStateComponent()
    {
        using var client = factory.CreateHttpsClient();

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
        using var client = factory.CreateHttpsClient();

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
        using var client = factory.CreateHttpsClient();

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
        return View("~/Views/Home/Index.cshtml");
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

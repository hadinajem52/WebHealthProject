using System.Net;
using System.Text.Json;
using FluentAssertions;
using WebHealth.Web.Ajax;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class LiveRunStatusTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task CrawlHistory_WithACrawlStillRunning_DeclaresItsLiveRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync(
            $"/Crawl?endpointId={EmptyCrawlReportReader.RunningEndpointId}");

        html.Should().Contain("id=\"crawl-results\"");
        html.Should().Contain("data-run-active=\"true\"");
        html.Should().Contain("data-run-also=\"#crawl-run-action\"");
        html.Should().Contain("id=\"crawl-run-action\"");
        html.Should().Contain("class=\"spinner spinner--badge\"",
            "a run still going carries a turning mark, not a still badge");
    }

    [Fact]
    public async Task CrawlHistory_WithNothingRunning_LeavesItsRegionInactive()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync(
            $"/Crawl?endpointId={EmptyTargetRegistryReader.Endpoint.Id}");

        html.Should().Contain("id=\"crawl-results\"");
        html.Should().Contain("data-run-active=\"false\"",
            "nothing is in progress, so nothing should poll");
        html.Should().NotContain("class=\"spinner");
    }

    [Fact]
    public async Task CrawlRun_WhileRunning_DeclaresItsLiveRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync(
            $"/Crawl/Run?id={EmptyCrawlReportReader.RunningRunId}");

        html.Should().Contain("data-run-active=\"true\"");
        html.Should().Contain("data-run-url=\"/Crawl/Run", "the poller refreshes this run's own page");
        html.Should().Contain("class=\"spinner\"");
        html.Should().Contain("id=\"crawl-broken-links-results\"");
        html.Should().Contain("data-live=\"pages\"");
        html.Should().Contain("data-live=\"links\"");
        html.Should().Contain("data-live=\"broken\"");
    }

    [Fact]
    public async Task CrawlHistory_PolledAsAFragment_CarriesBothReplacedRegions()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        var html = await client.GetStringAsync(
            $"/Crawl?endpointId={EmptyCrawlReportReader.RunningEndpointId}");

        html.Should().Contain("id=\"crawl-results\"");
        html.Should().Contain("id=\"crawl-run-action\"");
        html.Should().NotContain("<!DOCTYPE html>");
    }

    [Fact]
    public async Task CrawlLiveStatus_ReturnsCurrentCountersAndDisablesCaching()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync(
            $"/Crawl/Runs/{EmptyCrawlReportReader.RunningRunId}/Status");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        json.GetProperty("active").GetBoolean().Should().BeTrue();
        json.GetProperty("version").GetString().Should().Be("Running:3:12");
        json.GetProperty("pages").GetInt32().Should().Be(3);
        json.GetProperty("links").GetInt32().Should().Be(12);
        json.GetProperty("broken").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task CrawlLiveStatus_WithTheCurrentVersion_ReturnsNoContent()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync(
            $"/Crawl/Runs/{EmptyCrawlReportReader.RunningRunId}/Status?version=Running%3A3%3A12");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CrawlLiveResults_ReturnOnlyTheReplaceableTableRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync(
            $"/Crawl/Runs/{EmptyCrawlReportReader.RunningRunId}/Results");

        html.Should().Contain("id=\"crawl-broken-links-results\"");
        html.Should().Contain("data-ajax-region");
        html.Should().Contain("No broken links were recorded");
        html.Should().NotContain("id=\"ajax-page\"");
        html.Should().NotContain("id=\"crawl-broken-heading\"");
        html.Should().NotContain("<!DOCTYPE html>");
    }

    [Fact]
    public async Task CrawlHistory_ExposesLiveCountersOnlyOnTheActiveRow()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync(
            $"/Crawl?endpointId={EmptyCrawlReportReader.RunningEndpointId}");

        html.Should().Contain("data-live=\"pages\"");
        html.Should().Contain("data-live=\"broken\"");
    }

    [Fact]
    public async Task PageSpeed_WithAQueuedAudit_DeclaresItsLiveRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var html = await client.GetStringAsync(
            $"/PageAudits?endpointId={EmptyTargetRegistryReader.Endpoint.Id}&strategy=mobile");

        html.Should().Contain("id=\"page-audit-results\"");
        html.Should().Contain("data-run-active=\"true\"");
        html.Should().Contain("data-run-url=\"/PageAudits/Status");
    }
}

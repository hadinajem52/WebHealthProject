using FluentAssertions;
using WebHealth.Web.Ajax;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// The contract the browser poller reads. A page that shows work in progress declares the region
/// to refresh and the address to refresh it from; when nothing is in progress it declares itself
/// inactive so nothing polls. These are string attributes shared between Razor and JavaScript,
/// with no compiler between them, so a rename that misses one side is silent without this.
/// </summary>
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
    }

    /// <summary>
    /// The request the poller actually makes. It carries the AJAX header, so the response must be
    /// the fragment both regions are read from and nothing else: a full document here would leave
    /// the poller replacing regions with markup nested inside a second layout.
    /// </summary>
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

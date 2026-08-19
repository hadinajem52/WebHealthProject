using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

/// <summary>
/// AC-08's view: an endpoint's crawl history, its broken links with the pages that contain them,
/// and how the latest full-scope crawl compares to the one before it.
/// <para>
/// Read-only for every persona that may read the registry. The endpoint and run ids in the query
/// string are parameters, not permissions: the reader resolves both through the requester's
/// visibility scope, so an id belonging to another client reads as empty rather than as data.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class CrawlController(
    ICrawlReportReader crawlReader,
    ITargetRegistryReader targetReader) : Controller
{
    private const int RunsListed = 20;
    private const int BrokenLinksPerPage = 50;

    [HttpGet]
    public async Task<IActionResult> Index(Guid? endpointId, CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var endpoints = await targetReader.ListAllEndpointsAsync(access, null, cancellationToken);
        var options = endpoints
            .Select(endpoint => new EndpointOption(
                endpoint.Id,
                $"{endpoint.WebsiteName} · {endpoint.EnvironmentName} · {endpoint.DisplayUrl}"))
            .ToArray();

        // Selecting nothing shows the picker rather than an arbitrary endpoint's history: the page
        // should not imply that whichever endpoint sorted first is the one worth looking at.
        if (endpointId is not { } selected)
        {
            return View(new CrawlIndexViewModel(options, null, [], CrawlComparison.Empty));
        }

        var runs = await crawlReader.ListRunsAsync(selected, RunsListed, access, cancellationToken);
        var comparison = await crawlReader.CompareLatestAsync(selected, access, cancellationToken);
        return View(new CrawlIndexViewModel(options, selected, runs, comparison));
    }

    [HttpGet]
    public async Task<IActionResult> Run(Guid id, int offset = 0, CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var run = await crawlReader.FindRunAsync(id, access, cancellationToken);
        if (run is null)
        {
            // Not found rather than forbidden: telling an unauthorized caller that the run exists
            // is itself a disclosure.
            return NotFound();
        }

        var brokenLinks = await crawlReader.ListBrokenLinksAsync(
            id, BrokenLinksPerPage, access, Math.Max(0, offset), cancellationToken);
        return View(new CrawlRunViewModel(run, brokenLinks, Math.Max(0, offset), BrokenLinksPerPage));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

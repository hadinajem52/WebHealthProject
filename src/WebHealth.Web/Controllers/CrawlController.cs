using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Crawling;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

/// <summary>
/// AC-08's view: an endpoint's crawl history, its broken links with the pages that contain them,
/// and how the latest full-scope crawl compares to the one before it.
/// <para>
/// Reading is open to every persona that may read the registry. The endpoint and run ids in the
/// query string are parameters, not permissions: the reader resolves both through the requester's
/// visibility scope, so an id belonging to another client reads as empty rather than as data.
/// </para>
/// <para>
/// Starting a crawl is different. It fetches many pages of a site this application does not own,
/// so it needs the same permission a manual check needs and a service-level check on the endpoint
/// itself. It is also the only trigger a crawl has: nothing else enqueues one.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class CrawlController(
    ICrawlReportReader crawlReader,
    ITargetRegistryReader targetReader,
    ITargetAuthorizationService targetAuthorization,
    ICrawlRunner crawlRunner) : Controller
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

        // Offered only when this requester may test this endpoint, mirroring the authorization the
        // action itself enforces - the button is a convenience, not the control.
        var canRun = crawlRunner.CanQueue
            && await targetAuthorization.CanTestEndpointAsync(selected, access, cancellationToken);
        var activeRun = runs
            .FirstOrDefault(run => run.Status == CrawlRunStatuses.Running)?.RunId;

        return View(new CrawlIndexViewModel(options, selected, runs, comparison, canRun, activeRun));
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(Guid endpointId, CancellationToken cancellationToken = default)
    {
        var access = GetAccess();

        // The policy above says this user may test targets at all; the runner says they may test
        // *this* one. Without the second check an endpoint id in a form post would be permission
        // enough to make this application crawl a site.
        var result = await crawlRunner.QueueManualAsync(endpointId, access, cancellationToken);

        if (!result.Succeeded)
        {
            TempData.AddFlashMessage(FlashLevel.Warning, result.Error!);
        }
        else if (result.WasAlreadyRunning)
        {
            TempData.AddFlashMessage(
                FlashLevel.Information,
                "A crawl for this endpoint is already running. Showing that one.");
        }
        else
        {
            TempData.AddFlashMessage(
                FlashLevel.Success,
                "Crawl queued. It fetches the site page by page, so results appear as it goes.");
        }

        return RedirectToAction(nameof(Index), new { endpointId });
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
        var skips = await crawlReader.ListSkipReasonsAsync(id, access, cancellationToken);
        return View(new CrawlRunViewModel(
            run, brokenLinks, skips, Math.Max(0, offset), BrokenLinksPerPage));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

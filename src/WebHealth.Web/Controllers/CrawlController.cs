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

        if (endpointId is not { } selected)
        {
            return View(new CrawlIndexViewModel(options, null, [], CrawlComparison.Empty));
        }

        var runs = await crawlReader.ListRunsAsync(selected, RunsListed, access, cancellationToken);
        var comparison = await crawlReader.CompareLatestAsync(selected, access, cancellationToken);

        var block = await targetAuthorization.DescribeTestBlockAsync(selected, access, cancellationToken);
        var canRun = crawlRunner.CanQueue && block == EndpointTestBlock.None;
        var activeRun = runs
            .FirstOrDefault(run => run.Status == CrawlRunStatuses.Running)?.RunId;

        return View(new CrawlIndexViewModel(
            options,
            selected,
            runs,
            comparison,
            canRun,
            activeRun,
            block,
            crawlRunner.CanQueue));
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(
        Guid endpointId,
        bool checkExternalLinks,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();

        var result = await crawlRunner.QueueManualAsync(
            endpointId, access, checkExternalLinks, cancellationToken);

        if (!result.Succeeded)
        {
            TempData.AddFlashMessage(
                FlashLevel.Warning,
                result.Error
                    ?? EndpointTestBlockDisplay.Describe(result.Block, "a crawl")
                    ?? "This endpoint cannot be crawled right now.");
        }
        else if (result.WasAlreadyRunning)
        {
            TempData.AddFlashMessage(
                FlashLevel.Information,
                "A crawl for this endpoint is already running. Showing that one.");
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
            return this.NotFoundRecord("crawl run");
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

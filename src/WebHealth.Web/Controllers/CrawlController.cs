using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Archiving;
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
    IEndpointTestGate testGate,
    ICrawlRunner crawlRunner,
    IRunHistoryArchive runHistoryArchive) : Controller
{
    private const int RunsListed = 20;
    private const int BrokenLinksPerPage = 50;

    [HttpGet]
    public async Task<IActionResult> Index(Guid? endpointId, CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var endpoints = await targetReader.ListAllEndpointsAsync(access, cancellationToken: cancellationToken);
        var options = endpoints
            .Select(EndpointOption.For)
            .ToArray();

        if (endpointId is not { } selected)
        {
            return View(new CrawlIndexViewModel(
                options, null, [], CrawlComparison.Empty, CanArchive: CanArchive()));
        }

        var runs = await crawlReader.ListRunsAsync(
            selected, RunsListed, access, cancellationToken: cancellationToken);
        var comparison = await crawlReader.CompareLatestAsync(selected, access, cancellationToken);

        var block = await testGate.DescribeTestBlockAsync(selected, access, cancellationToken);
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
            crawlRunner.CanQueue,
            CanArchive()));
    }

    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearHistory(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var result = await runHistoryArchive.ArchiveFinishedAsync(
            RunHistoryArea.Crawl, new(endpointId), GetAccess(), cancellationToken);
        return this.ArchiveOutcome(
            result, Url.Action(nameof(Index), new { endpointId })!, "crawl");
    }

    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreRun(
        Guid endpointId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await runHistoryArchive.RestoreAsync(
            RunHistoryArea.Crawl, id, GetAccess(), cancellationToken);
        return this.RestoreOutcome(
            result, Url.Action(nameof(Archived), new { endpointId })!, "crawl");
    }

    [HttpGet]
    public async Task<IActionResult> Archived(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var endpoints = await targetReader.ListAllEndpointsAsync(
            access, cancellationToken: cancellationToken);
        if (!endpoints.Any(endpoint => endpoint.Id == endpointId))
        {
            return this.NotFoundRecord("endpoint");
        }

        var runs = await crawlReader.ListRunsAsync(
            endpointId, RunsListed, access, archivedOnly: true, cancellationToken);
        return View(
            "RunHistoryArchiveScreen",
            new RunHistoryArchiveScreenViewModel(
                "Crawl archive",
                "Crawls cleared from the history list. Nothing is deleted: every crawl keeps its "
                + "recorded links, and restoring one puts it back on the crawl history unchanged.",
                "Subject",
                ["Broken links", "Robots override"],
                [.. runs.Select(run => new RunHistoryArchiveRow(
                    run.RunId,
                    $"{run.PagesFetched} page{(run.PagesFetched == 1 ? null : "s")} crawled",
                    run.StartedAt.ToLocalTime().ToString("d MMM yyyy HH:mm"),
                    new StatusBadgeViewModel(
                        CrawlRunDisplay.StatusTone(run),
                        CrawlRunDisplay.DescribeStatus(run),
                        CrawlRunDisplay.DescribeStopReason(run)),
                    [
                        run.BrokenLinkCount.ToString(),
                        run.RobotsOverrideGranted ? "Granted" : "Not granted"
                    ],
                    Url.Action(nameof(Run), new { id = run.RunId })!))],
                "crawl",
                "Broken links",
                Url.Action(nameof(Index), new { endpointId })!,
                Url.Action(nameof(RestoreRun), new { endpointId })!,
                CanArchive(),
                "The archive is empty",
                "Clearing the crawl history for this endpoint moves its finished crawls here.",
                "#ajax-page"));
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

    [HttpGet("Crawl/Runs/{id:guid}/Status")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LiveStatus(
        Guid id,
        string? version,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var run = await crawlReader.GetLiveStatusAsync(id, access, cancellationToken);
        if (run is null) return this.NotFoundRecord("crawl run");

        var currentVersion = $"{run.Status}:{run.PagesFetched}:{run.LinksRecorded}";
        if (string.Equals(version, currentVersion, StringComparison.Ordinal)) return NoContent();

        var broken = await crawlReader.CountBrokenLinksAsync(id, access, cancellationToken);
        return Json(new
        {
            active = run.Status == CrawlRunStatuses.Running,
            version = currentVersion,
            pages = run.PagesFetched,
            links = run.LinksRecorded,
            broken
        });
    }

    [HttpGet("Crawl/Runs/{id:guid}/Results")]
    public async Task<IActionResult> Results(
        Guid id,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var run = await crawlReader.GetLiveStatusAsync(id, access, cancellationToken);
        if (run is null) return this.NotFoundRecord("crawl run");

        var normalizedOffset = Math.Max(0, offset);
        var brokenLinks = await crawlReader.ListBrokenLinksAsync(
            id, BrokenLinksPerPage, access, normalizedOffset, cancellationToken);
        return PartialView("_BrokenLinksTable", new CrawlBrokenLinksRegionViewModel(
            id, brokenLinks, normalizedOffset, BrokenLinksPerPage, run.CoveredWholeScope));
    }

    private bool CanArchive() =>
        User.IsInRole(ApplicationRoles.Administrator)
        || User.IsInRole(ApplicationRoles.Operations);

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

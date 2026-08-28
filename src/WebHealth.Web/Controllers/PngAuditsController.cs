using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Archiving;
using WebHealth.Application.Authorization;
using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Ajax;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
[Route("Tools/PngImages")]
public sealed class PngAuditsController(
    IPngAuditReader pngAuditReader,
    ITargetRegistryReader targetReader,
    IEndpointTestGate testGate,
    IPngAuditRunner pngAuditRunner,
    IRunHistoryArchive runHistoryArchive) : Controller
{
    private const int RunsListed = 20;
    private const int ImagesPerPage = 50;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid? endpointId,
        CancellationToken cancellationToken = default)
    {
        var model = await BuildIndexModelAsync(endpointId, GetAccess(), cancellationToken);
        return model is null ? this.NotFoundRecord("endpoint") : View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets)]
    [HttpPost("Run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        if (!await testGate.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            var block = await testGate.DescribeTestBlockAsync(endpointId, access, cancellationToken);
            return this.AjaxMessage(
                Url.Action(nameof(Index), new { endpointId })!,
                EndpointTestBlockDisplay.Describe(block, "a PNG image audit")
                    ?? "This endpoint cannot be audited right now.",
                FlashLevel.Error,
                block is EndpointTestBlock.NotVisible or EndpointTestBlock.NotPermitted
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status422UnprocessableEntity);
        }

        var result = await pngAuditRunner.QueueManualAsync(endpointId, access, cancellationToken);
        var message = result.WasAlreadyRunning
            ? "A PNG image audit for this endpoint is already running. Showing that run."
            : result.Error
                ?? EndpointTestBlockDisplay.Describe(result.Block, "a PNG image audit");

        if (Request.IsWebHealthAjax())
        {
            if (!result.Succeeded)
            {
                return StatusCode(
                    StatusCodes.Status422UnprocessableEntity,
                    new AjaxFragmentViewModel(
                        message ?? "This endpoint cannot be audited right now.",
                        "warning"));
            }

            return Accepted(new AjaxFragmentViewModel(
                message,
                "information",
                RefreshUrl: Url.Action(nameof(Index), new { endpointId }),
                StatusUrl: Url.Action(nameof(LiveStatus), new { id = result.RunId }),
                RunId: result.RunId));
        }

        if (message is not null)
        {
            TempData.AddFlashMessage(
                result.Succeeded ? FlashLevel.Information : FlashLevel.Warning,
                message);
        }

        return RedirectToAction(nameof(Index), new { endpointId });
    }

    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring)]
    [HttpPost("Archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearHistory(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var result = await runHistoryArchive.ArchiveFinishedAsync(
            RunHistoryArea.PngAudit, new(endpointId), GetAccess(), cancellationToken);
        return this.ArchiveOutcome(
            result, Url.Action(nameof(Index), new { endpointId })!, "PNG image audit run");
    }

    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring)]
    [HttpPost("Archive/Restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreRun(
        Guid endpointId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await runHistoryArchive.RestoreAsync(
            RunHistoryArea.PngAudit, id, GetAccess(), cancellationToken);
        return this.RestoreOutcome(
            result, Url.Action(nameof(Archived), new { endpointId })!, "PNG image audit run");
    }

    [HttpGet("Archive")]
    public async Task<IActionResult> Archived(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var runs = await pngAuditReader.ListRunsAsync(
            endpointId, RunsListed, access, archivedOnly: true, cancellationToken);
        var endpoints = await targetReader.ListAllEndpointsAsync(
            access, cancellationToken: cancellationToken);
        if (!endpoints.Any(endpoint => endpoint.Id == endpointId))
        {
            return this.NotFoundRecord("endpoint");
        }

        return View(
            "RunHistoryArchiveScreen",
            new RunHistoryArchiveScreenViewModel(
                "PNG image audit archive",
                "Audit runs cleared from the history list. Nothing is deleted: every run keeps its "
                + "image results, and restoring one puts it back on the audit history unchanged.",
                "Run",
                ["Pages", "Unique images", "WebP candidates"],
                [.. runs.Select(run => new RunHistoryArchiveRow(
                    run.RunId,
                    "PNG image audit",
                    $"Queued {run.QueuedAt.ToLocalTime():d MMM yyyy HH:mm}",
                    new StatusBadgeViewModel(
                        PngAuditDisplay.StatusTone(run),
                        PngAuditDisplay.DescribeStatus(run),
                        PngAuditDisplay.DescribeStatusDetail(run)),
                    [
                        run.PagesDiscovered.ToString(),
                        run.ImagesDiscovered.ToString(),
                        run.RecommendationCount.ToString()
                    ],
                    Url.Action(nameof(Run), new { id = run.RunId })!))],
                "run",
                "PNG image audits",
                Url.Action(nameof(Index), new { endpointId })!,
                Url.Action(nameof(RestoreRun), new { endpointId })!,
                CanArchive(),
                "The archive is empty",
                "Clearing the audit history for this endpoint moves its finished runs here.",
                "#ajax-page"));
    }

    [HttpGet("Status")]
    public async Task<IActionResult> Status(
        Guid endpointId,
        Guid? runId,
        bool details = false,
        string? filter = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        if (details && runId is { } detailsRunId)
        {
            var runModel = await BuildRunModelAsync(
                detailsRunId,
                filter,
                offset,
                access,
                cancellationToken);
            if (runModel is null || runModel.Run.EndpointId != endpointId)
            {
                return this.NotFoundRecord("PNG image audit run");
            }

            Response.StatusCode = PngAuditRunStatuses.IsActive(runModel.Run.Status)
                ? StatusCodes.Status202Accepted
                : StatusCodes.Status200OK;
            return View(nameof(Run), runModel);
        }

        if (runId is { } requestedRunId)
        {
            var requestedRun = await pngAuditReader.FindRunAsync(
                requestedRunId,
                access,
                cancellationToken);
            if (requestedRun is null || requestedRun.EndpointId != endpointId)
            {
                return this.NotFoundRecord("PNG image audit run");
            }
        }

        var model = await BuildIndexModelAsync(endpointId, access, cancellationToken);
        if (model is null)
        {
            return this.NotFoundRecord("endpoint");
        }

        Response.StatusCode = model.ActiveRun is null
            ? StatusCodes.Status200OK
            : StatusCodes.Status202Accepted;
        return View(nameof(Index), model);
    }

    [HttpGet("Runs/{id:guid}")]
    public async Task<IActionResult> Run(
        Guid id,
        string? filter = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var model = await BuildRunModelAsync(
            id,
            filter,
            offset,
            GetAccess(),
            cancellationToken);
        return model is null ? this.NotFoundRecord("PNG image audit run") : View(model);
    }

    [HttpGet("Runs/{id:guid}/Status")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LiveStatus(
        Guid id,
        string? version,
        CancellationToken cancellationToken = default)
    {
        var run = await pngAuditReader.GetLiveStatusAsync(id, GetAccess(), cancellationToken);
        if (run is null)
        {
            return this.NotFoundRecord("PNG image audit run");
        }

        var currentVersion = PngAuditDisplay.LiveVersion(run);
        if (string.Equals(version, currentVersion, StringComparison.Ordinal))
        {
            return NoContent();
        }

        return Json(new
        {
            active = PngAuditRunStatuses.IsActive(run.Status),
            version = currentVersion,
            status = PngAuditDisplay.DescribeStatus(run.Status),
            pages = run.PagesDiscovered,
            resultCount = run.ImagesDiscovered,
            recommendations = run.RecommendationCount
        });
    }

    [HttpGet("Runs/{id:guid}/Results")]
    public async Task<IActionResult> Results(
        Guid id,
        string? filter = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var run = await pngAuditReader.GetLiveStatusAsync(id, access, cancellationToken);
        if (run is null)
        {
            return this.NotFoundRecord("PNG image audit run");
        }

        var selectedFilter = PngAuditImageFilters.Normalize(filter);
        var images = await pngAuditReader.ListImagesByFilterAsync(
            id,
            selectedFilter,
            Math.Max(0, offset),
            ImagesPerPage,
            access,
            cancellationToken);
        return PartialView(
            "_ImageResults",
            new PngAuditResultsRegionViewModel(id, selectedFilter, images));
    }

    private async Task<PngAuditIndexViewModel?> BuildIndexModelAsync(
        Guid? endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var endpoints = await targetReader.ListAllEndpointsAsync(
            access,
            cancellationToken: cancellationToken);
        var options = endpoints
            .Select(EndpointOption.For)
            .ToArray();
        var canExecute = CanExecute();
        if (endpointId is not { } selected)
        {
            return new(options, null, [], canExecute, pngAuditRunner.CanQueue, CanArchive());
        }

        if (!options.Any(option => option.Id == selected))
        {
            return null;
        }

        var runs = await pngAuditReader.ListRunsAsync(
            selected,
            RunsListed,
            access,
            cancellationToken: cancellationToken);
        var block = canExecute
            ? await testGate.DescribeTestBlockAsync(selected, access, cancellationToken)
            : EndpointTestBlock.None;
        return new(
            options, selected, runs, canExecute, pngAuditRunner.CanQueue, CanArchive(), block);
    }

    private async Task<PngAuditRunViewModel?> BuildRunModelAsync(
        Guid runId,
        string? filter,
        int offset,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var run = await pngAuditReader.FindRunAsync(runId, access, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var selectedFilter = PngAuditImageFilters.Normalize(filter);
        var summary = await pngAuditReader.GetResultSummaryAsync(
            runId,
            access,
            cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var images = await pngAuditReader.ListImagesByFilterAsync(
            runId,
            selectedFilter,
            Math.Max(0, offset),
            ImagesPerPage,
            access,
            cancellationToken);
        var coverageReasons = await pngAuditReader.ListCoverageReasonsAsync(
            runId,
            access,
            cancellationToken);
        return new(run, summary, images, coverageReasons, selectedFilter);
    }

    private bool CanArchive() =>
        User.IsInRole(ApplicationRoles.Administrator)
        || User.IsInRole(ApplicationRoles.Operations);

    private bool CanExecute() =>
        User.IsInRole(ApplicationRoles.Administrator)
        || User.IsInRole(ApplicationRoles.Operations)
        || User.IsInRole(ApplicationRoles.DeveloperSupport);

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        return new(
            userId,
            ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

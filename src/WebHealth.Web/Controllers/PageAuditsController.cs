using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;
using WebHealth.Web.Ajax;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class PageAuditsController(
    IPageAuditReader pageAuditReader,
    ITargetRegistryReader targetReader,
    IEndpointTestGate testGate,
    IPageAuditRunner pageAuditRunner) : Controller
{
    private const int RunsListed = 20;

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? endpointId,
        string? category,
        string? strategy,
        Guid? runId,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var selectedCategory = PageAuditCategories.Normalize(category);
        var selectedStrategy = PageAuditStrategies.Normalize(strategy);
        var model = await BuildModelAsync(
            endpointId,
            selectedCategory,
            selectedStrategy,
            runId,
            access,
            cancellationToken);
        return model is null ? this.NotFoundRecord("endpoint") : View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(
        Guid endpointId,
        string? category,
        string? strategy,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var selectedCategory = PageAuditCategories.Normalize(category);

        var selectedStrategy = PageAuditStrategies.Normalize(strategy);

        if (!await testGate.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            var block = await testGate.DescribeTestBlockAsync(
                endpointId, access, cancellationToken);
            return this.AjaxMessage(
                Url.Action(nameof(Index), new { endpointId })!,
                EndpointTestBlockDisplay.Describe(block, "a PageSpeed audit")
                    ?? "This endpoint cannot be audited right now.",
                FlashLevel.Error,
                block is EndpointTestBlock.NotVisible or EndpointTestBlock.NotPermitted
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status422UnprocessableEntity);
        }

        var result = await pageAuditRunner.QueueManualAsync(
            endpointId, access, cancellationToken);

        string? message = null;
        var level = FlashLevel.Information;
        if (!result.Succeeded)
        {
            message = result.Error
                ?? EndpointTestBlockDisplay.Describe(result.Block, "a PageSpeed audit")
                ?? "This endpoint cannot be audited right now.";
            level = FlashLevel.Warning;
        }
        else if (result.WasAlreadyRunning)
        {
            message = "A PageSpeed audit for this endpoint is already running. Showing that one.";
        }
        else if (result.AlreadyRunningCount > 0)
        {
            message = "PageSpeed audit queued for the profiles that were not already running.";
        }

        if (Request.IsWebHealthAjax())
        {
            if (!result.Succeeded)
            {
                return StatusCode(
                    StatusCodes.Status422UnprocessableEntity,
                    new AjaxFragmentViewModel(message, "warning"));
            }

            var summary = await pageAuditReader.GetEndpointSummaryAsync(
                endpointId,
                selectedCategory,
                selectedStrategy,
                null,
                access,
                cancellationToken);
            var runId = summary?.LatestRun?.RunId;
            return Accepted(new AjaxFragmentViewModel(
                message,
                level.ToString().ToLowerInvariant(),
                StatusUrl: Url.Action(nameof(Status), new { endpointId, category = selectedCategory, strategy = selectedStrategy, runId }),
                RunId: runId));
        }

        if (message is not null)
        {
            TempData.AddFlashMessage(level, message);
        }

        return RedirectToAction(nameof(Index), new { endpointId, category = selectedCategory, strategy = selectedStrategy });
    }

    [HttpGet]
    public async Task<IActionResult> Status(
        Guid endpointId,
        string? category,
        string? strategy,
        Guid? runId,
        CancellationToken cancellationToken = default)
    {
        var selectedCategory = PageAuditCategories.Normalize(category);
        var selectedStrategy = PageAuditStrategies.Normalize(strategy);
        var model = await BuildModelAsync(
            endpointId,
            selectedCategory,
            selectedStrategy,
            runId,
            GetAccess(),
            cancellationToken);
        if (model is null)
        {
            return this.NotFoundRecord("endpoint");
        }

        Response.StatusCode = model.AnyCategoryRunActive
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status200OK;
        return View(nameof(Index), model);
    }

    private async Task<PageAuditIndexViewModel?> BuildModelAsync(
        Guid? endpointId,
        string category,
        string strategy,
        Guid? runId,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var endpoints = await targetReader.ListAllEndpointsAsync(access, cancellationToken: cancellationToken);
        var options = endpoints
            .Select(endpoint => new EndpointOption(
                endpoint.Id,
                $"{endpoint.WebsiteName} · {endpoint.EnvironmentName} · {endpoint.DisplayUrl}"))
            .ToArray();
        if (endpointId is not { } selected)
        {
            return new(options, null, category, strategy, null, [], [], [], false);
        }

        var summary = await pageAuditReader.GetEndpointSummaryAsync(
            selected,
            category,
            strategy,
            runId,
            access,
            cancellationToken);
        if (summary is null || runId is not null && summary.LatestRun is null)
        {
            return null;
        }

        var runs = await pageAuditReader.ListRunsAsync(
            selected,
            category,
            strategy,
            RunsListed,
            access,
            cancellationToken);
        var items = summary.LatestRun is null
            ? []
            : await pageAuditReader.ListAuditItemsAsync(
                summary.LatestRun.RunId,
                access,
                cancellationToken);
        var block = await testGate.DescribeTestBlockAsync(selected, access, cancellationToken);
        var canRun = summary.IsEnabled && block == EndpointTestBlock.None;
        var categorySummaries = await pageAuditReader.GetLatestCategorySummariesAsync(
            selected,
            strategy,
            access,
            cancellationToken);
        if (categorySummaries is null)
        {
            return null;
        }

        return new(options, selected, category, strategy, summary, categorySummaries, runs, items, canRun, block);
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

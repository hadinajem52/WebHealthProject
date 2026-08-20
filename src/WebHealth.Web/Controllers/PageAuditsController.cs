using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

/// <summary>
/// One endpoint's Lighthouse technical SEO score, the audits behind it, and its run history.
/// </summary>
/// <remarks>
/// <para>
/// Reading is open to every persona that may read the registry. The endpoint and run ids in the
/// query string are parameters, not permissions: the reader resolves both through the requester's
/// visibility scope in the database, so an id belonging to another client reads as absent rather
/// than as data.
/// </para>
/// <para>
/// Running an audit is different. It asks Google to load a configured target, which is active
/// testing of that target, so it needs the same permission a manual check needs and a
/// service-level check on the endpoint itself.
/// </para>
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class PageAuditsController(
    IPageAuditReader pageAuditReader,
    ITargetRegistryReader targetReader,
    ITargetAuthorizationService targetAuthorization,
    IPageAuditRunner pageAuditRunner) : Controller
{
    private const int RunsListed = 20;

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? endpointId,
        Guid? runId,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var endpoints = await targetReader.ListAllEndpointsAsync(access, null, cancellationToken);
        var options = endpoints
            .Select(endpoint => new EndpointOption(
                endpoint.Id,
                $"{endpoint.WebsiteName} · {endpoint.EnvironmentName} · {endpoint.DisplayUrl}"))
            .ToArray();

        // Selecting nothing shows the picker rather than an arbitrary endpoint's score: the page
        // should not imply that whichever endpoint sorted first is the one worth looking at.
        if (endpointId is not { } selected)
        {
            return View(new PageAuditIndexViewModel(options, null, null, [], [], false));
        }

        var summary = await pageAuditReader.GetEndpointSummaryAsync(
            selected, runId, access, cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        var runs = await pageAuditReader.ListRunsAsync(selected, RunsListed, access, cancellationToken);
        var items = summary.LatestRun is null
            ? []
            : await pageAuditReader.ListAuditItemsAsync(summary.LatestRun.RunId, access, cancellationToken);

        // Whether to offer Run now is decided here rather than in the view, and it is the same
        // authorization the action itself enforces - the button is a convenience, not the control.
        var canRun = summary.IsEnabled
            && await targetAuthorization.CanTestEndpointAsync(selected, access, cancellationToken);

        return View(new PageAuditIndexViewModel(options, selected, summary, runs, items, canRun));
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(Guid endpointId, CancellationToken cancellationToken = default)
    {
        var access = GetAccess();

        // The policy above says this user may test targets at all; this says they may test *this*
        // one. Without the second check an endpoint id in a form post would be permission enough.
        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return NotFound();
        }

        var result = await pageAuditRunner.QueueManualAsync(
            endpointId, access.UserId, cancellationToken);

        if (!result.Succeeded)
        {
            TempData.AddFlashMessage(FlashLevel.Warning, result.Error!);
        }
        else if (result.WasAlreadyRunning)
        {
            TempData.AddFlashMessage(
                FlashLevel.Information,
                "A PageSpeed audit for this endpoint is already running. Showing that one.");
        }
        else
        {
            TempData.AddFlashMessage(
                FlashLevel.Success,
                "PageSpeed audit queued. Google runs the audit, so the score appears once it answers.");
        }

        return RedirectToAction(nameof(Index), new { endpointId, runId = result.RunId });
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

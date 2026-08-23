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
        string? strategy,
        Guid? runId,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();

        // Normalized rather than validated: the strategy names which of two measurements of the
        // same page to read, so an unrecognised one is a wrong address that mobile answers, not
        // an error worth a page.
        var selectedStrategy = PageAuditStrategies.Normalize(strategy);
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
            return View(new PageAuditIndexViewModel(
                options, null, selectedStrategy, null, [], [], false));
        }

        var summary = await pageAuditReader.GetEndpointSummaryAsync(
            selected, selectedStrategy, runId, access, cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        // A run id that names nothing this endpoint owns is a wrong address, not an endpoint with
        // no history. Rendering "no audit has run yet" would answer a different question than the
        // one asked, and would read as though the run had been deleted.
        if (runId is not null && summary.LatestRun is null)
        {
            return NotFound();
        }

        var runs = await pageAuditReader.ListRunsAsync(
            selected, selectedStrategy, RunsListed, access, cancellationToken);
        var items = summary.LatestRun is null
            ? []
            : await pageAuditReader.ListAuditItemsAsync(summary.LatestRun.RunId, access, cancellationToken);

        // Whether to offer Run now is decided here rather than in the view, and it is the same
        // authorization the action itself enforces - the button is a convenience, not the control.
        var canRun = summary.IsEnabled
            && await targetAuthorization.CanTestEndpointAsync(selected, access, cancellationToken);

        return View(new PageAuditIndexViewModel(
            options, selected, selectedStrategy, summary, runs, items, canRun));
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(
        Guid endpointId,
        string? strategy,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();

        // One request audits every form factor, so the strategy here only decides which of the
        // two results the reader lands on afterwards.
        var selectedStrategy = PageAuditStrategies.Normalize(strategy);

        // The policy above says this user may test targets at all; this says they may test *this*
        // one. Without the second check an endpoint id in a form post would be permission enough.
        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return NotFound();
        }

        var result = await pageAuditRunner.QueueManualAsync(
            endpointId, access, cancellationToken);

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
            // Counted, not named. One audit covers every form factor, so a partial answer is
            // "the rest was already running" rather than a form factor the reader has to chase.
            var partial = result.AlreadyRunningCount > 0
                ? " The rest was already running."
                : null;
            TempData.AddFlashMessage(
                FlashLevel.Success,
                "PageSpeed audit queued for mobile and desktop. Google runs each form factor, "
                + $"so the scores appear once it answers.{partial}");
        }

        // No run id: the reader shows the newest run on the selected strategy, which is the one
        // this request just opened. Naming one would land a two-strategy request on a single form
        // factor's run and read as though the other had not been asked for.
        return RedirectToAction(nameof(Index), new { endpointId, strategy = selectedStrategy });
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

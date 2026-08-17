using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class ChecksController(
    IManualCheckService manualCheckService,
    ICheckHistoryReader checkHistoryReader) : Controller
{
    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost]
    public async Task<IActionResult> RunCheck(Guid id, CancellationToken cancellationToken)
    {
        var result = await manualCheckService.RunNowAsync(id, GetAccess(), cancellationToken);
        switch (result.Status)
        {
            case ManualCheckStatus.Forbidden:
                return Forbid();
            case ManualCheckStatus.MonitorNotAvailable:
                TempData.AddFlashMessage(FlashLevel.Error, "This endpoint has no active monitor to run.");
                break;
            case ManualCheckStatus.SchedulingUnavailable:
                TempData.AddFlashMessage(FlashLevel.Error, "Manual checks are unavailable while monitoring scheduling is disabled.");
                break;
            default:
                TempData.AddFlashMessage(FlashLevel.Success, "Check queued. It will appear in history shortly.");
                break;
        }

        return RedirectToAction(nameof(TargetsController.Endpoint), "Targets", new { id });
    }

    [HttpGet]
    public async Task<IActionResult> History(Guid id, int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await checkHistoryReader.ListForEndpointAsync(id, GetAccess(), page, cancellationToken);
        return result is null ? NotFound() : View(new CheckHistoryViewModel(result));
    }

    [HttpGet]
    public async Task<IActionResult> Check(Guid id, CancellationToken cancellationToken)
    {
        var check = await checkHistoryReader.FindCheckAsync(id, GetAccess(), cancellationToken);
        return check is null ? NotFound() : View(new CheckDetailsViewModel(check));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

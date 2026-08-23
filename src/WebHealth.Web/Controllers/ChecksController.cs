using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;
using WebHealth.Web.Ajax;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class ChecksController(
    IManualCheckService manualCheckService,
    ICheckHistoryReader checkHistoryReader,
    TimeProvider timeProvider) : Controller
{
    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost]
    public async Task<IActionResult> RunCheck(Guid id, CancellationToken cancellationToken)
    {
        var result = await manualCheckService.RunNowAsync(id, GetAccess(), cancellationToken);
        return HandleManualCheckResult(id, result, "Availability check queued. It will appear in history shortly.");
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost]
    public async Task<IActionResult> RunCertificateCheck(Guid id, CancellationToken cancellationToken)
    {
        var result = await manualCheckService.RunCertificateNowAsync(id, GetAccess(), cancellationToken);
        return HandleManualCheckResult(id, result, "Certificate check queued. It will appear in history shortly.");
    }

    private IActionResult HandleManualCheckResult(Guid endpointId, ManualCheckResult result, string successMessage)
    {
        var endpointUrl = Url.Action(nameof(TargetsController.Endpoint), "Targets", new { id = endpointId })!;
        switch (result.Status)
        {
            case ManualCheckStatus.Forbidden:
                return Forbid();
            case ManualCheckStatus.MonitorNotAvailable:
                return this.AjaxMessage(
                    endpointUrl,
                    "This endpoint has no active monitor to run.",
                    FlashLevel.Error,
                    StatusCodes.Status422UnprocessableEntity);
            case ManualCheckStatus.SchedulingUnavailable:
                return this.AjaxMessage(
                    endpointUrl,
                    "Manual checks are unavailable while monitoring scheduling is disabled.",
                    FlashLevel.Error,
                    StatusCodes.Status422UnprocessableEntity);
        }

        if (Request.IsWebHealthAjax())
        {
            return StatusCode(
                StatusCodes.Status202Accepted,
                new AjaxFragmentViewModel(
                    successMessage,
                    "success",
                    StatusUrl: Url.Action(nameof(Status), new { id = result.LogicalCheckId }),
                    RunId: result.LogicalCheckId));
        }

        TempData.AddFlashMessage(FlashLevel.Success, successMessage);
        return LocalRedirect(endpointUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken)
    {
        var check = await checkHistoryReader.FindCheckAsync(id, GetAccess(), cancellationToken);
        if (check is null)
        {
            return NotFound();
        }

        var isComplete = check.State == LogicalCheckStates.Completed;
        return StatusCode(
            isComplete ? StatusCodes.Status200OK : StatusCodes.Status202Accepted,
            new AjaxFragmentViewModel(
                isComplete ? "Check completed." : null,
                "success",
                RefreshUrl: isComplete
                    ? Url.Action(nameof(TargetsController.Endpoint), "Targets", new { id = check.EndpointId })
                    : null,
                StatusUrl: Url.Action(nameof(Status), new { id }),
                RunId: id));
    }

    [HttpGet]
    public async Task<IActionResult> History(Guid id, int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await checkHistoryReader.ListForEndpointAsync(id, GetAccess(), page, cancellationToken);
        // BR-R01: the page states which endpoint it is scoped to and when it was read.
        return result is null
            ? NotFound()
            : View(new CheckHistoryViewModel(
                result,
                new FilterSummaryViewModel(
                    timeProvider.GetUtcNow(),
                    [new FilterSummaryItem("Endpoint", result.EndpointDisplayUrl)])));
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

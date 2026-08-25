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
        return HandleManualCheckResult(id, result);
    }

    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets), HttpPost]
    public async Task<IActionResult> RunCertificateCheck(Guid id, CancellationToken cancellationToken)
    {
        var result = await manualCheckService.RunCertificateNowAsync(id, GetAccess(), cancellationToken);
        return HandleManualCheckResult(id, result);
    }

    private IActionResult HandleManualCheckResult(Guid endpointId, ManualCheckResult result)
    {
        var endpointUrl = Url.Action(nameof(TargetsController.Endpoint), "Targets", new { id = endpointId })!;
        switch (result.Status)
        {
            case ManualCheckStatus.Forbidden:
                return this.AjaxMessage(
                    endpointUrl,
                    EndpointTestBlockDisplay.Describe(result.Block, "this check")
                        ?? "This endpoint cannot be checked right now.",
                    FlashLevel.Error,
                    result.Block is EndpointTestBlock.NotVisible or EndpointTestBlock.NotPermitted
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status422UnprocessableEntity,
                    endpointUrl);
            case ManualCheckStatus.MonitorNotAvailable:
                return this.AjaxMessage(
                    endpointUrl,
                    "This endpoint has no active monitor to run. Open Edit endpoint and enable the "
                    + "check you want before running it.",
                    FlashLevel.Error,
                    StatusCodes.Status422UnprocessableEntity,
                    endpointUrl);
            case ManualCheckStatus.SchedulingUnavailable:
                return this.AjaxMessage(
                    endpointUrl,
                    "Checks are not running on this instance, so none can be started. "
                    + "Enable Monitoring:Scheduling to run them.",
                    FlashLevel.Error,
                    StatusCodes.Status422UnprocessableEntity);
        }

        if (Request.IsWebHealthAjax())
        {
            return StatusCode(
                StatusCodes.Status202Accepted,
                new AjaxFragmentViewModel(
                    StatusUrl: Url.Action(nameof(Status), new { id = result.LogicalCheckId }),
                    RunId: result.LogicalCheckId));
        }

        return LocalRedirect(endpointUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken)
    {
        var check = await checkHistoryReader.FindCheckAsync(id, GetAccess(), cancellationToken);
        if (check is null)
        {
            return this.NotFoundRecord("check");
        }

        var isComplete = check.State == LogicalCheckStates.Completed;
        return StatusCode(
            isComplete ? StatusCodes.Status200OK : StatusCodes.Status202Accepted,
            new AjaxFragmentViewModel(
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
        return result is null
            ? this.NotFoundRecord("endpoint")
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
        return check is null ? this.NotFoundRecord("check") : View(new CheckDetailsViewModel(check));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

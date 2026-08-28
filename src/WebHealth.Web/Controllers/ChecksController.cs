using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Archiving;
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
    IRunHistoryArchive runHistoryArchive,
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
        var result = await checkHistoryReader.ListForEndpointAsync(
            id, GetAccess(), page, cancellationToken: cancellationToken);
        return result is null
            ? this.NotFoundRecord("endpoint")
            : View(new CheckHistoryViewModel(
                result,
                new FilterSummaryViewModel(
                    timeProvider.GetUtcNow(),
                    [new FilterSummaryItem("Endpoint", result.EndpointDisplayUrl)]),
                CanArchive()));
    }

    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearHistory(Guid id, CancellationToken cancellationToken)
    {
        var result = await runHistoryArchive.ArchiveFinishedAsync(
            RunHistoryArea.Check, new(id), GetAccess(), cancellationToken);
        return this.ArchiveOutcome(
            result, Url.Action(nameof(History), new { id })!, "check");
    }

    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreCheck(
        Guid endpointId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await runHistoryArchive.RestoreAsync(
            RunHistoryArea.Check, id, GetAccess(), cancellationToken);
        return this.RestoreOutcome(
            result, Url.Action(nameof(Archived), new { id = endpointId })!, "check");
    }

    [HttpGet]
    public async Task<IActionResult> Archived(
        Guid id,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await checkHistoryReader.ListForEndpointAsync(
            id, GetAccess(), page, archivedOnly: true, cancellationToken);
        if (result is null)
        {
            return this.NotFoundRecord("endpoint");
        }

        return View(
            "RunHistoryArchiveScreen",
            new RunHistoryArchiveScreenViewModel(
                "Check archive",
                "Checks cleared from the history list for "
                + $"{result.EndpointDisplayUrl}. Nothing is deleted: every check keeps its "
                + "recorded result and still counts towards uptime, and restoring one puts it "
                + "back on the check history unchanged.",
                "Subject",
                ["Initiator", "Duration", "HTTP status", "Uptime"],
                [.. result.Items.Select(item => new RunHistoryArchiveRow(
                    item.LogicalCheckId,
                    $"{item.Source} check",
                    (item.ScheduledFor ?? item.RequestedAt)?.ToLocalTime()
                        .ToString("d MMM yyyy HH:mm:ss"),
                    new StatusBadgeViewModel(
                        StatusBadges.ForOutcome(item.Outcome),
                        item.Outcome ?? item.State,
                        item.FailureCategory),
                    [
                        item.InitiatedByDisplayName ?? "—",
                        item.TotalDurationMs is { } duration ? $"{duration} ms" : "—",
                        item.HttpStatus?.ToString() ?? "—",
                        item.CountsForUptime ? "Counts" : "Excluded"
                    ],
                    Url.Action(nameof(Check), new { id = item.LogicalCheckId })!))],
                "check",
                "Check history",
                Url.Action(nameof(History), new { id })!,
                Url.Action(nameof(RestoreCheck), new { endpointId = id })!,
                CanArchive(),
                "The archive is empty",
                "Clearing the check history for this endpoint moves its completed checks here.",
                "#ajax-page"));
    }

    private bool CanArchive() =>
        User.IsInRole(ApplicationRoles.Administrator)
        || User.IsInRole(ApplicationRoles.Operations);

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

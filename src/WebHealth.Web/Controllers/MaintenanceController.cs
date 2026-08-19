using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Maintenance;
using WebHealth.Application.Registry;
using WebHealth.Domain.Maintenance;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.OperateMonitoring)]
public sealed class MaintenanceController(
    IMaintenanceReader maintenanceReader,
    IMaintenanceWindowService maintenanceService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new MaintenanceListViewModel(await maintenanceReader.ListAsync(cancellationToken)));

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var window = await maintenanceReader.FindAsync(id, cancellationToken);
        return window is null ? NotFound() : View(new MaintenanceDetailsViewModel(window));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(await BuildFormAsync(new MaintenanceWindowFormViewModel(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(MaintenanceWindowFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || model.ScopeId is null)
        {
            if (model.ScopeId is null) ModelState.AddModelError(nameof(model.ScopeId), "Select the maintenance target.");
            return View(await BuildFormAsync(model, cancellationToken));
        }

        var result = await maintenanceService.CreateAsync(ToCreate(model), GetAccess(), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(await BuildFormAsync(model, cancellationToken));
        }

        TempData.AddFlashMessage(FlashLevel.Success, "Maintenance window created.");
        return RedirectToAction(nameof(Details), new { id = result.MaintenanceWindowId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var window = await maintenanceReader.FindAsync(id, cancellationToken);
        if (window is null) return NotFound();
        if (window.IsCancelled)
        {
            TempData.AddFlashMessage(FlashLevel.Error, "Cancelled maintenance windows cannot be edited.");
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(await BuildFormAsync(new MaintenanceWindowFormViewModel
        {
            MaintenanceWindowId = window.Id,
            ScopeKind = window.Scope.Kind,
            ScopeId = window.Scope.TargetId,
            StartsAtUtc = window.StartsAt.UtcDateTime,
            EndsAtUtc = window.EndsAt.UtcDateTime,
            RecurrencePattern = window.Recurrence.Pattern,
            RecurrenceDays = ToDaySelections(window.Recurrence.DaysOfWeekMask),
            RecurrenceUntilUtc = window.Recurrence.Until?.UtcDateTime,
            TimezoneId = window.TimezoneId,
            Reason = window.Reason,
            SuppressionPolicy = window.SuppressionPolicy,
            PauseEscalation = window.PauseEscalation,
            ContinueFailureCounter = window.ContinueFailureCounter,
            Version = window.Version
        }, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(MaintenanceWindowFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || model.ScopeId is null)
        {
            if (model.ScopeId is null) ModelState.AddModelError(nameof(model.ScopeId), "Select the maintenance target.");
            return View(await BuildFormAsync(model, cancellationToken));
        }

        var result = await maintenanceService.UpdateAsync(new(
            model.MaintenanceWindowId, model.Version, new(model.ScopeKind, model.ScopeId.Value),
            ToUtc(model.StartsAtUtc), ToUtc(model.EndsAtUtc), model.TimezoneId, model.Reason,
            model.SuppressionPolicy, model.PauseEscalation, model.ContinueFailureCounter,
            ToRecurrence(model)), GetAccess(), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Status == MaintenanceMutationStatus.NotFound) return NotFound();
            AddErrors(result.Errors);
            return View(await BuildFormAsync(model, cancellationToken));
        }

        TempData.AddFlashMessage(FlashLevel.Success, "Maintenance window updated as a new immutable occurrence.");
        return RedirectToAction(nameof(Details), new { id = result.MaintenanceWindowId });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id, long version, CancellationToken cancellationToken)
    {
        var result = await maintenanceService.CancelAsync(new(id, version), GetAccess(), cancellationToken);
        TempData.AddFlashMessage(result.Succeeded ? FlashLevel.Success : FlashLevel.Error,
            result.Succeeded ? "Maintenance window cancelled. Existing check evidence is retained." : string.Join(" ", result.Errors));
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<MaintenanceWindowFormViewModel> BuildFormAsync(MaintenanceWindowFormViewModel model, CancellationToken cancellationToken)
    {
        model.ScopeOptions = await maintenanceReader.ListScopeOptionsAsync(cancellationToken);
        return model;
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }

    private static CreateMaintenanceWindow ToCreate(MaintenanceWindowFormViewModel model) => new(
        new(model.ScopeKind, model.ScopeId!.Value), ToUtc(model.StartsAtUtc), ToUtc(model.EndsAtUtc), model.TimezoneId,
        model.Reason, model.SuppressionPolicy, model.PauseEscalation, model.ContinueFailureCounter, ToRecurrence(model));

    // The submitted pattern is passed through unchanged so an unsupported value is rejected by
    // the application service rather than silently downgraded to a one-off window here. Only the
    // fields that depend on the pattern are cleared.
    private static MaintenanceRecurrenceSpec ToRecurrence(MaintenanceWindowFormViewModel model) =>
        model.RecurrencePattern == MaintenanceRecurrencePatterns.None
            ? new(MaintenanceRecurrencePatterns.None, MaintenanceDayOfWeekMask.Empty, null)
            : new(model.RecurrencePattern,
                model.RecurrencePattern == MaintenanceRecurrencePatterns.Weekly
                    ? model.RecurrenceDaysMask
                    : MaintenanceDayOfWeekMask.Empty,
                model.RecurrenceUntilUtc is { } until ? ToUtc(until) : null);

    private static bool[] ToDaySelections(int mask) => Enum.GetValues<DayOfWeek>()
        .Select(day => MaintenanceDayOfWeekMask.Includes(mask, day)).ToArray();

    private static DateTimeOffset ToUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors) ModelState.AddModelError(string.Empty, error);
    }
}

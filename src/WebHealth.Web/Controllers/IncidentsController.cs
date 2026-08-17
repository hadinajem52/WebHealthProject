using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class IncidentsController(
    IIncidentReader incidentReader,
    IIncidentLifecycleService incidentLifecycle,
    IRegistryReader registryReader) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? status,
        string? severity,
        bool unacknowledgedOnly,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await incidentReader.ListAsync(
            new(status, severity, unacknowledgedOnly), GetAccess(), page, cancellationToken);
        return View(new IncidentListViewModel(result, status, severity, unacknowledgedOnly));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var incident = await incidentReader.FindAsync(id, GetAccess(), cancellationToken);
        if (incident is null)
        {
            return NotFound();
        }

        var owners = incident.CanManage
            ? await registryReader.ListOwnersAsync(incident.OwnerSubjectId, cancellationToken)
            : [];
        return View(new IncidentDetailsViewModel(incident, owners));
    }

    [HttpPost]
    public async Task<IActionResult> Acknowledge(Guid id, long version, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id, () => incidentLifecycle.AcknowledgeAsync(new(id, version), GetAccess(), cancellationToken),
            "Incident acknowledged.");

    [HttpPost]
    public async Task<IActionResult> StartProgress(Guid id, long version, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id, () => incidentLifecycle.StartProgressAsync(new(id, version), GetAccess(), cancellationToken),
            "Incident moved in progress.");

    [HttpPost]
    public async Task<IActionResult> Reassign(
        Guid id, long version, Guid ownerSubjectId, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id,
            () => incidentLifecycle.ReassignAsync(new(id, version, ownerSubjectId), GetAccess(), cancellationToken),
            "Incident reassigned.");

    [HttpPost]
    public async Task<IActionResult> AddNote(Guid id, long version, string note, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id, () => incidentLifecycle.AddNoteAsync(new(id, version, note), GetAccess(), cancellationToken),
            "Note added.");

    [HttpPost]
    public async Task<IActionResult> Resolve(
        Guid id, long version, string category, string note, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id,
            () => incidentLifecycle.ResolveAsync(new(id, version, category, note), GetAccess(), cancellationToken),
            "Incident resolved.");

    [HttpPost]
    public async Task<IActionResult> Close(Guid id, long version, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id, () => incidentLifecycle.CloseAsync(new(id, version), GetAccess(), cancellationToken),
            "Incident closed.");

    [HttpPost]
    public async Task<IActionResult> ForceClose(
        Guid id, long version, string reason, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id,
            () => incidentLifecycle.ForceCloseAsync(new(id, version, reason), GetAccess(), cancellationToken),
            "Incident force-closed.");

    [HttpPost]
    public async Task<IActionResult> Reopen(
        Guid id, long version, string reason, CancellationToken cancellationToken) =>
        await ApplyAsync(
            id, () => incidentLifecycle.ReopenAsync(new(id, version, reason), GetAccess(), cancellationToken),
            "Incident reopened.");

    private async Task<IActionResult> ApplyAsync(
        Guid id, Func<Task<IncidentMutationResult>> mutate, string successMessage)
    {
        var result = await mutate();
        switch (result.Status)
        {
            case IncidentMutationStatus.Succeeded:
                TempData.AddFlashMessage(FlashLevel.Success, successMessage);
                break;
            case IncidentMutationStatus.Forbidden:
                return Forbid();
            case IncidentMutationStatus.NotFound:
                return NotFound();
            default:
                TempData.AddFlashMessage(FlashLevel.Error, string.Join(" ", result.Errors));
                break;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

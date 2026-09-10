using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;
using WebHealth.Web.Ajax;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class IncidentsController(
    IIncidentReader incidentReader,
    IIncidentLifecycleService incidentLifecycle,
    IRegistryReader registryReader,
    TimeProvider timeProvider) : Controller
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
        return View(new IncidentListViewModel(
            result,
            status,
            severity,
            unacknowledgedOnly,
            IncidentListViewModel.Describe(
                timeProvider.GetUtcNow(), status, severity, unacknowledgedOnly),
            User.IsInRole(ApplicationRoles.Administrator) || User.IsInRole(ApplicationRoles.Operations)));
    }

    [HttpGet]
    public async Task<IActionResult> Archived(int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await incidentReader.ListAsync(
            new(ArchivedOnly: true), GetAccess(), page, cancellationToken);
        return View(new IncidentArchiveViewModel(result, User.IsInRole(ApplicationRoles.Administrator)
            || User.IsInRole(ApplicationRoles.Operations)));
    }

    [HttpPost]
    public async Task<IActionResult> ArchiveResolved(CancellationToken cancellationToken)
    {
        var result = await incidentLifecycle.ArchiveResolvedAsync(GetAccess(), cancellationToken);
        var indexUrl = Url.Action(nameof(Index))!;
        return result.Status switch
        {
            IncidentMutationStatus.Forbidden => Forbid(),
            IncidentMutationStatus.Succeeded => this.RedirectOrAjaxRefresh(
                indexUrl,
                indexUrl,
                result.ArchivedCount == 0
                    ? "There was nothing resolved to archive."
                    : $"{result.ArchivedCount} incident{(result.ArchivedCount == 1 ? null : "s")} moved to the archive.",
                result.ArchivedCount == 0 ? FlashLevel.Information : FlashLevel.Success),
            _ => this.RedirectOrAjaxRefresh(
                indexUrl,
                indexUrl,
                string.Join(" ", result.Errors),
                FlashLevel.Error,
                StatusCodes.Status409Conflict)
        };
    }

    [HttpPost]
    public async Task<IActionResult> Restore(Guid id, long version, CancellationToken cancellationToken)
    {
        var result = await incidentLifecycle.RestoreAsync(new(id, version), GetAccess(), cancellationToken);
        var archivedUrl = Url.Action(nameof(Archived))!;
        return result.Status switch
        {
            IncidentMutationStatus.Forbidden => Forbid(),
            IncidentMutationStatus.NotFound => this.NotFoundRecord("incident"),
            IncidentMutationStatus.Succeeded => this.RedirectOrAjaxRefresh(
                archivedUrl, archivedUrl, "Incident restored from the archive.", FlashLevel.Success),
            IncidentMutationStatus.ConcurrencyConflict => this.RedirectOrAjaxRefresh(
                archivedUrl,
                archivedUrl,
                string.Join(" ", result.Errors),
                FlashLevel.Error,
                StatusCodes.Status409Conflict),
            _ => this.AjaxMessage(
                archivedUrl,
                string.Join(" ", result.Errors),
                FlashLevel.Error,
                StatusCodes.Status422UnprocessableEntity,
                archivedUrl)
        };
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        Guid id,
        int timelinePage = 1,
        int samplePage = 1,
        CancellationToken cancellationToken = default)
    {
        var incident = await incidentReader.FindAsync(
            id, GetAccess(), timelinePage, samplePage, cancellationToken);
        if (incident is null)
        {
            return this.NotFoundRecord("incident");
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
        var detailsUrl = Url.Action(nameof(Details), new { id })!;
        switch (result.Status)
        {
            case IncidentMutationStatus.Succeeded:
                return this.RedirectOrAjaxRefresh(
                    detailsUrl,
                    detailsUrl,
                    successMessage,
                    FlashLevel.Success);
            case IncidentMutationStatus.Forbidden:
                return Forbid();
            case IncidentMutationStatus.NotFound:
                return this.NotFoundRecord("incident");
            case IncidentMutationStatus.ConcurrencyConflict:
                return this.RedirectOrAjaxRefresh(
                    detailsUrl,
                    detailsUrl,
                    string.Join(" ", result.Errors),
                    FlashLevel.Error,
                    StatusCodes.Status409Conflict);
            default:
                return this.AjaxMessage(
                    detailsUrl,
                    string.Join(" ", result.Errors),
                    FlashLevel.Error,
                    StatusCodes.Status422UnprocessableEntity,
                    detailsUrl);
        }
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}

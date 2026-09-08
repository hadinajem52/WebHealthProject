using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ManageRegistry)]
public sealed class TargetPermissionsController(ITargetPermissionService permissions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid id, CancellationToken token)
    {
        var page = await permissions.ReadAsync(id, Access(), token);
        return page is null ? NotFound() : View(new TargetPermissionsViewModel { EndpointId = id, Url = page.Url, Permissions = page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(TargetPermissionsViewModel model, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            var result = await permissions.GrantAsync(new(model.EndpointId, model.Url, model.Kind, model.EvidenceReference, model.ExpiresAt), Access(), token);
            if (result.Succeeded) return RedirectToAction(nameof(Index), new { id = model.EndpointId });
            if (result.Status == RegistryMutationStatus.Forbidden) return Forbid();
            foreach (var error in result.Errors) ModelState.AddModelError(error.Field ?? "", error.Message);
        }
        model.Permissions = await permissions.ReadAsync(model.EndpointId, Access(), token);
        return model.Permissions is null ? NotFound() : View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid endpointId, Guid permissionId, string reason, CancellationToken token)
    {
        var result = await permissions.RevokeAsync(endpointId, permissionId, reason, Access(), token);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { id = endpointId });
        if (result.Status == RegistryMutationStatus.Forbidden) return Forbid();
        var page = await permissions.ReadAsync(endpointId, Access(), token);
        if (page is null || result.Status == RegistryMutationStatus.NotFound) return NotFound();
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Message);
        return View("Index", new TargetPermissionsViewModel { EndpointId = endpointId, Url = page.Url, Permissions = page });
    }

    private RegistryAccessContext Access() => new(
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
        ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
}

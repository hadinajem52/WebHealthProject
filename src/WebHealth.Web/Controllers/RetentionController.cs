using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Administration)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RetentionController(IRetentionHoldService holds, TimeProvider timeProvider) : Controller
{
    [HttpGet]
    public Task<IActionResult> Index(int offset, CancellationToken cancellationToken) =>
        RenderAsync(new() { Offset = Math.Max(0, offset) }, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RetentionHoldsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await RenderAsync(model, cancellationToken);
        var expiry = model.ExpiresAt is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : (DateTimeOffset?)null;
        var result = await holds.CreateAsync(new(model.ScopeType, model.ScopeId, model.Reason, expiry), Access(), cancellationToken);
        if (result.Status == RegistryMutationStatus.Forbidden) return Forbid();
        if (result.Succeeded) return RedirectToAction(nameof(Index));
        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Message);
        return await RenderAsync(model, cancellationToken);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken)
    {
        var result = await holds.ReleaseAsync(id, Access(), cancellationToken);
        if (result.Status == RegistryMutationStatus.Forbidden) return Forbid();
        if (result.Status == RegistryMutationStatus.NotFound) return NotFound();
        if (result.Succeeded) return RedirectToAction(nameof(Index));
        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Message);
        return await RenderAsync(new(), cancellationToken);
    }

    private async Task<IActionResult> RenderAsync(RetentionHoldsViewModel model, CancellationToken cancellationToken)
    {
        model.Offset = Math.Max(0, model.Offset);
        model.AsOf = timeProvider.GetUtcNow();
        try { model.Holds = await holds.ListAsync(Access(), model.Offset, cancellationToken); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return View("Index", model);
    }

    private RegistryAccessContext Access() => new(
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
        ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.PageAudits;
using WebHealth.Web.Ajax;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Administration)]
public sealed class PageAuditSettingsController(
    IPageAuditIncidentPolicyService policyService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(PageAuditIncidentSettingsViewModel.From(
            await policyService.GetAsync(cancellationToken)));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        PageAuditIncidentSettingsViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await policyService.UpdateAsync(
            model.ToCommand(), GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(PageAuditIncidentSettingsViewModel.From(result.Policy));
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Index))!,
            "PageSpeed incident settings saved.");
    }

    private Guid GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}

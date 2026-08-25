using WebHealth.Application;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Ajax;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Administration)]
public sealed class PageAuditSettingsController(
    IPageAuditIncidentPolicyService policyService,
    ITargetRegistryReader targetReader) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var endpoint = await FindEndpointAsync(endpointId, cancellationToken);
        if (endpoint is null)
        {
            return this.NotFoundRecord("endpoint");
        }

        return View(PageAuditIncidentSettingsViewModel.From(
            await policyService.GetAsync(endpointId, cancellationToken),
            Describe(endpoint)));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        PageAuditIncidentSettingsViewModel model,
        CancellationToken cancellationToken)
    {
        var endpoint = await FindEndpointAsync(model.EndpointId, cancellationToken);
        if (endpoint is null)
        {
            return this.NotFoundRecord("endpoint");
        }

        if (!ModelState.IsValid)
        {
            model.EndpointLabel = Describe(endpoint);
            return View(model);
        }

        var result = await policyService.UpdateAsync(
            model.EndpointId, model.ToCommand(), GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            model.EndpointLabel = Describe(endpoint);
            if (result.Conflict)
            {
                ModelState.Clear();
                model = PageAuditIncidentSettingsViewModel.From(result.Policy, Describe(endpoint));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Field ?? string.Empty, error.Message);
            }

            return this.ValidationView(
                nameof(Index),
                model,
                result.Conflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity);
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Index), new { endpointId = model.EndpointId })!,
            "PageSpeed incident settings saved.");
    }

    private async Task<RegistryEndpointItem?> FindEndpointAsync(
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var endpoints = await targetReader.ListAllEndpointsAsync(
            GetAccess(), null, cancellationToken);
        return endpoints.SingleOrDefault(endpoint => endpoint.Id == endpointId);
    }

    private static string Describe(RegistryEndpointItem endpoint) =>
        $"{endpoint.WebsiteName} · {endpoint.EnvironmentName} · {endpoint.DisplayUrl}";

    private RegistryAccessContext GetAccess() => new(
        GetActorUserId(),
        ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());

    private Guid GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}

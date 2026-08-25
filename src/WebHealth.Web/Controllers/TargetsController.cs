using WebHealth.Application;
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

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class TargetsController(
    IRegistryReader registryReader,
    ITargetRegistryReader targetReader,
    IEnvironmentRegistryService environmentService,
    IEndpointRegistryService endpointService,
    ICheckHistoryReader checkHistoryReader) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Endpoints(string? search, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new RegistryEndpointListViewModel(
            await targetReader.ListAllEndpointsAsync(access, search, cancellationToken),
            search));
    }

    [HttpGet]
    public async Task<IActionResult> Environments(Guid websiteId, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        var website = await registryReader.FindWebsiteAsync(websiteId, access, cancellationToken);
        if (website is null)
        {
            return this.NotFoundRecord("website");
        }

        return View(new EnvironmentListViewModel(
            website.Id,
            website.Name,
            await targetReader.ListEnvironmentsAsync(website.Id, access, cancellationToken),
            CanManage(access)));
    }

    [HttpGet]
    public async Task<IActionResult> Environment(Guid id, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        var environment = await targetReader.FindEnvironmentAsync(id, access, cancellationToken);
        return environment is null ? this.NotFoundRecord("environment") : View(new EnvironmentDetailsViewModel(environment, CanManage(access)));
    }

    [HttpGet]
    public async Task<IActionResult> Endpoint(Guid id, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        var endpoint = await targetReader.FindEndpointAsync(id, access, cancellationToken);
        if (endpoint is null)
        {
            return this.NotFoundRecord("endpoint");
        }

        var latestCheck = await checkHistoryReader.FindLatestForEndpointAsync(id, access, cancellationToken);
        var certificate = await targetReader.FindCertificateStatusAsync(id, access, cancellationToken);
        return View(new EndpointDetailsViewModel(
            endpoint,
            CanManage(access),
            User.IsInRole(ApplicationRoles.Administrator),
            latestCheck,
            certificate ?? CertificateStatus.NotApplicable));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> Archived(CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new TargetArchiveViewModel(
            await targetReader.ListDeletedEnvironmentsAsync(access, cancellationToken),
            await targetReader.ListDeletedEndpointsAsync(access, cancellationToken),
            User.IsInRole(ApplicationRoles.Administrator)));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> CreateEnvironment(Guid websiteId, CancellationToken cancellationToken)
    {
        var website = await registryReader.FindWebsiteAsync(websiteId, GetAccess(), cancellationToken);
        return website is null
            ? this.NotFoundRecord("website")
            : View(new EnvironmentFormViewModel { WebsiteId = website.Id, WebsiteName = website.Name });
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> CreateEnvironment(EnvironmentFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await RestoreWebsiteNameAsync(model, cancellationToken);
            return this.ValidationView(nameof(CreateEnvironment), model);
        }

        var result = await environmentService.CreateAsync(
            new(model.WebsiteId, model.Name, model.EnvironmentType, model.BaseUrl, model.IsActive),
            GetAccess(), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            await RestoreWebsiteNameAsync(model, cancellationToken);
            return this.ValidationView(nameof(CreateEnvironment), model);
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Environment), new { id = result.EntityId })!,
            "Environment created successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> EditEnvironment(Guid id, CancellationToken cancellationToken)
    {
        var environment = await targetReader.FindEnvironmentAsync(id, GetAccess(), cancellationToken);
        return environment is null ? this.NotFoundRecord("environment") : View(new EnvironmentFormViewModel
        {
            EnvironmentId = environment.Id,
            WebsiteId = environment.WebsiteId,
            WebsiteName = environment.WebsiteName,
            Name = environment.Name,
            EnvironmentType = environment.EnvironmentType,
            BaseUrl = environment.BaseUrl,
            IsActive = environment.IsActive,
            Version = environment.Version
        });
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> EditEnvironment(EnvironmentFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await RestoreWebsiteNameAsync(model, cancellationToken);
            return this.ValidationView(nameof(EditEnvironment), model);
        }

        var result = await environmentService.UpdateAsync(
            new(model.EnvironmentId, model.Name, model.EnvironmentType, model.BaseUrl, model.IsActive, model.Version),
            GetAccess(), cancellationToken);
        if (!result.Succeeded)
        {
            return await HandleEnvironmentFailureAsync(model, result, cancellationToken);
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Environment), new { id = model.EnvironmentId })!,
            "Environment updated successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> CreateEndpoint(Guid environmentId, CancellationToken cancellationToken)
    {
        var environment = await targetReader.FindEnvironmentAsync(environmentId, GetAccess(), cancellationToken);
        return environment is null ? this.NotFoundRecord("environment") : View(await BuildEndpointFormAsync(new EndpointFormViewModel
        {
            EnvironmentId = environment.Id,
            EnvironmentName = environment.Name,
            IsProduction = environment.IsProduction
        }, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> CreateEndpoint(EndpointFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(CreateEndpoint),
                await BuildEndpointFormAsync(model, cancellationToken));
        }

        var result = await endpointService.CreateAsync(
            new(model.EnvironmentId, model.Url, model.OwnerSubjectId, model.IsEnabled, model.HttpExceptionReason,
                model.TargetAuthorizationKind, model.TargetAuthorizationEvidence, model.TargetAuthorizationExpiresAt,
                model.IntervalMinutesOverride, model.SchedulingEnabled,
                model.WarningThresholdMsOverride, model.CriticalThresholdMsOverride,
                model.SeoExpectedCanonicalHost, model.SeoIndexingExpectation, model.SeoDescriptionRequired,
                model.PageAuditEnabled, model.PageAuditSchedulingEnabled, model.PageAuditIntervalHours),
            GetAccess(), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return this.ValidationView(
                nameof(CreateEndpoint),
                await BuildEndpointFormAsync(model, cancellationToken));
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Endpoint), new { id = result.EntityId })!,
            "Endpoint and HTTP monitor created successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> EditEndpoint(Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await targetReader.FindEndpointAsync(id, GetAccess(), cancellationToken);
        return endpoint is null ? this.NotFoundRecord("endpoint") : View(await BuildEndpointFormAsync(new EndpointFormViewModel
        {
            EndpointId = endpoint.Id,
            EnvironmentId = endpoint.EnvironmentId,
            EnvironmentName = endpoint.EnvironmentName,
            IsProduction = endpoint.IsProduction,
            Url = endpoint.DisplayUrl,
            OwnerSubjectId = endpoint.OwnerSubjectId,
            IsEnabled = endpoint.IsEnabled,
            HttpExceptionReason = endpoint.HttpExceptionReason,
            TargetAuthorizationKind = endpoint.TargetAuthorizationKind,
            TargetAuthorizationEvidence = endpoint.TargetAuthorizationEvidence,
            TargetAuthorizationExpiresAt = endpoint.TargetAuthorizationExpiresAt,
            SchedulingEnabled = endpoint.SchedulingEnabled,
            IntervalMinutesOverride = endpoint.IntervalMinutesOverride,
            // Only a real override is echoed back, so re-saving an unchanged form does not
            // freeze today's default into the endpoint as an explicit choice.
            WarningThresholdMsOverride = endpoint.HasThresholdOverride ? endpoint.WarningThresholdMs : null,
            CriticalThresholdMsOverride = endpoint.HasThresholdOverride ? endpoint.CriticalThresholdMs : null,
            SeoExpectedCanonicalHost = endpoint.SeoExpectedCanonicalHost,
            SeoIndexingExpectation = endpoint.SeoIndexingExpectation,
            SeoDescriptionRequired = endpoint.SeoDescriptionRequired,
            PageAuditEnabled = endpoint.PageAuditEnabled,
            PageAuditSchedulingEnabled = endpoint.PageAuditSchedulingEnabled,
            PageAuditIntervalHours = endpoint.PageAuditIntervalHours,
            Version = endpoint.Version
        }, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> EditEndpoint(EndpointFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(EditEndpoint),
                await BuildEndpointFormAsync(model, cancellationToken));
        }

        var result = await endpointService.UpdateAsync(
            new(model.EndpointId, model.Url, model.OwnerSubjectId, model.IsEnabled, model.HttpExceptionReason,
                model.TargetAuthorizationKind, model.TargetAuthorizationEvidence,
                model.TargetAuthorizationExpiresAt, model.Version,
                model.IntervalMinutesOverride, model.SchedulingEnabled,
                model.WarningThresholdMsOverride, model.CriticalThresholdMsOverride,
                model.SeoExpectedCanonicalHost, model.SeoIndexingExpectation, model.SeoDescriptionRequired,
                model.PageAuditEnabled, model.PageAuditSchedulingEnabled, model.PageAuditIntervalHours),
            GetAccess(), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Status == RegistryMutationStatus.NotFound)
            {
                return this.NotFoundRecord("endpoint");
            }

            AddErrors(result.Errors);
            return this.ValidationView(
                nameof(EditEndpoint),
                await BuildEndpointFormAsync(model, cancellationToken),
                result.Status == RegistryMutationStatus.ConcurrencyConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity);
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Endpoint), new { id = model.EndpointId })!,
            "Endpoint updated successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DisableEnvironment(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, environmentService.DisableAsync, nameof(Environment), nameof(Environment), "Environment disabled.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DeleteEnvironment(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, environmentService.DeleteAsync, nameof(Archived), nameof(Environment), "Environment archived.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> RestoreEnvironment(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, environmentService.RestoreAsync, nameof(Archived), nameof(Environment), "Environment restored in a disabled state.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DisableEndpoint(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, endpointService.DisableAsync, nameof(Endpoint), nameof(Endpoint), "Endpoint disabled.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DeleteEndpoint(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, endpointService.DeleteAsync, nameof(Archived), nameof(Endpoint), "Endpoint archived.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> PauseEndpointSchedule(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, endpointService.PauseScheduleAsync, nameof(Endpoint),
            nameof(Endpoint), "Scheduled checks paused. Manual runs are still available.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> ResumeEndpointSchedule(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, endpointService.ResumeScheduleAsync, nameof(Endpoint),
            nameof(Endpoint), "Scheduled checks resumed.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> RestoreEndpoint(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, endpointService.RestoreAsync, nameof(Archived), nameof(Endpoint), "Endpoint restored in a disabled state.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpPost]
    public Task<IActionResult> PurgeEndpoint(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, version, endpointService.PurgeAsync, nameof(Archived),
            nameof(Endpoint), "Endpoint permanently deleted with all of its monitoring history.", cancellationToken);

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }

    private static bool CanManage(RegistryAccessContext access) => RegistryVisibilityRoleNames.Any(access.Roles.Contains);
    private static readonly string[] RegistryVisibilityRoleNames = [ApplicationRoles.Administrator, ApplicationRoles.Operations];

    private async Task<EndpointFormViewModel> BuildEndpointFormAsync(EndpointFormViewModel model, CancellationToken cancellationToken)
    {
        var environment = await targetReader.FindEnvironmentAsync(model.EnvironmentId, GetAccess(), cancellationToken);
        model.EnvironmentName = environment?.Name ?? model.EnvironmentName;
        model.WebsiteId = environment?.WebsiteId ?? model.WebsiteId;
        model.WebsiteName = environment?.WebsiteName ?? model.WebsiteName;
        model.IsProduction = environment?.IsProduction ?? model.IsProduction;
        model.CanApproveHttp = User.IsInRole(ApplicationRoles.Administrator);
        model.CanConfigureInterval = User.IsInRole(ApplicationRoles.Administrator);
        model.Owners = await registryReader.ListOwnersAsync(model.OwnerSubjectId, cancellationToken);
        return model;
    }

    private async Task RestoreWebsiteNameAsync(EnvironmentFormViewModel model, CancellationToken cancellationToken)
    {
        var website = await registryReader.FindWebsiteAsync(model.WebsiteId, GetAccess(), cancellationToken);
        model.WebsiteName = website?.Name ?? model.WebsiteName;
    }

    private async Task<IActionResult> HandleEnvironmentFailureAsync(EnvironmentFormViewModel model, RegistryMutationResult result, CancellationToken cancellationToken)
    {
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord("endpoint");
        }

        AddErrors(result.Errors);
        await RestoreWebsiteNameAsync(model, cancellationToken);
        return this.ValidationView(
            nameof(EditEnvironment),
            model,
            result.Status == RegistryMutationStatus.ConcurrencyConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity);
    }

    private async Task<IActionResult> ChangeStateAsync(
        Guid id, long version,
        Func<RegistryVersionCommand, RegistryAccessContext, CancellationToken, Task<RegistryMutationResult>> operation,
        string redirectAction, string detailsAction, string successMessage, CancellationToken cancellationToken)
    {
        var result = await operation(new(id, version), GetAccess(), cancellationToken);
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord("endpoint");
        }

        var message = result.Succeeded ? successMessage : string.Join(" ", result.Errors);
        var level = result.Succeeded ? FlashLevel.Success : FlashLevel.Error;
        if (Request.IsWebHealthAjax())
        {
            var statusCode = result.Succeeded
                ? StatusCodes.Status200OK
                : result.Status == RegistryMutationStatus.ConcurrencyConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(
                statusCode,
                new AjaxFragmentViewModel(
                    message,
                    level.ToString().ToLowerInvariant(),
                    RefreshUrl: Url.Action(detailsAction, new { id })));
        }

        TempData.AddFlashMessage(level, message);
        return RedirectToAction(redirectAction, redirectAction is nameof(Environment) or nameof(Endpoint) ? new { id } : null);
    }

    private void AddErrors(IEnumerable<ValidationError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message);
        }
    }
}

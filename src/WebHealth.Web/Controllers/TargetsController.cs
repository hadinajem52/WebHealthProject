using WebHealth.Application;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Domain.Normalization;
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
    IEndpointRegistrationService endpointRegistrationService,
    ICheckHistoryReader checkHistoryReader,
    ITargetAuthorizationService targetAuthorization) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Endpoints(
        [FromQuery] EndpointRegistryFilter filter,
        CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new RegistryEndpointListViewModel(
            await targetReader.ListAllEndpointsAsync(access, filter, cancellationToken: cancellationToken),
            filter,
            await registryReader.ListClientsAsync(access, cancellationToken),
            await registryReader.ListWebsitesAsync(access, cancellationToken: cancellationToken),
            await targetReader.ListAllEnvironmentsAsync(access, cancellationToken),
            CanManage(access),
            User.IsInRole(ApplicationRoles.Administrator)));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> RegisterEndpoint(
        Guid? clientId,
        Guid? websiteId,
        Guid? environmentId,
        CancellationToken cancellationToken)
    {
        var model = await BuildRegistrationFormAsync(new(), cancellationToken);
        if (environmentId is { } selectedEnvironmentId)
        {
            if (!model.Environments.Any(environment => environment.Id == selectedEnvironmentId))
            {
                return this.NotFoundRecord("environment");
            }

            model.HierarchyMode = EndpointRegistrationModes.ExistingEnvironment;
            model.EnvironmentId = selectedEnvironmentId;
        }
        else if (websiteId is { } selectedWebsiteId)
        {
            if (!model.Websites.Any(website => website.Id == selectedWebsiteId))
            {
                return this.NotFoundRecord("website");
            }

            model.HierarchyMode = EndpointRegistrationModes.NewEnvironment;
            model.WebsiteId = selectedWebsiteId;
        }
        else if (clientId is { } selectedClientId)
        {
            if (!model.Clients.Any(client => client.Id == selectedClientId))
            {
                return this.NotFoundRecord("client");
            }

            model.HierarchyMode = EndpointRegistrationModes.NewWebsite;
            model.ClientId = selectedClientId;
        }
        else
        {
            SelectDefaultRegistrationMode(model);
        }

        return View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> RegisterEndpoint(
        EndpointRegistrationFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(RegisterEndpoint),
                await BuildRegistrationFormAsync(model, cancellationToken));
        }

        var result = await endpointRegistrationService.RegisterAsync(
            new RegisterEndpointRequest(
                BuildRegistrationHierarchy(model),
                BuildRegistrationSettings(model)),
            GetAccess(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return this.ValidationView(
                nameof(RegisterEndpoint),
                await BuildRegistrationFormAsync(model, cancellationToken));
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Endpoint), new { id = result.EntityId })!,
            "Endpoint registered and ready for monitoring.");
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
        var testBlock = await targetAuthorization.DescribeTestBlockAsync(id, access, cancellationToken);
        return View(new EndpointDetailsViewModel(
            endpoint,
            CanManage(access),
            User.IsInRole(ApplicationRoles.Administrator),
            latestCheck,
            certificate ?? CertificateStatus.NotApplicable,
            testBlock));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> ArchiveEndpoints(CancellationToken cancellationToken) =>
        View(RegistryActionScreens.ViewName, BuildEndpointScreen(
            await targetReader.ListAllEndpointsAsync(GetAccess(), cancellationToken: cancellationToken),
            destructive: false));

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpGet]
    public async Task<IActionResult> DeleteEndpoints(CancellationToken cancellationToken) =>
        View(RegistryActionScreens.ViewName, BuildEndpointScreen(
            await targetReader.ListAllEndpointsAsync(
                GetAccess(), includeArchived: true, cancellationToken: cancellationToken),
            destructive: true));

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> Archived(CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new TargetArchiveViewModel(
            await targetReader.ListDeletedEnvironmentsAsync(access, cancellationToken),
            await targetReader.ListDeletedEndpointsAsync(access, cancellationToken)));
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

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> ArchiveEndpoint(Guid id, long version, CancellationToken cancellationToken) =>
        RunScreenActionAsync(id, version, endpointService.DeleteAsync, nameof(ArchiveEndpoints),
            "Endpoint archived and its checks stopped.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpPost]
    public Task<IActionResult> PurgeEndpoint(Guid id, long version, CancellationToken cancellationToken) =>
        RunScreenActionAsync(id, version, endpointService.PurgeAsync, nameof(DeleteEndpoints),
            "Endpoint permanently deleted with all of its monitoring history.", cancellationToken);

    private static RegistryActionScreenViewModel BuildEndpointScreen(
        IReadOnlyList<RegistryEndpointItem> endpoints,
        bool destructive) => new(
        destructive ? "Delete endpoints" : "Archive endpoints",
        destructive
            ? "Permanent deletion removes the endpoint with all of its checks, results, SEO observations, crawls, incidents and notifications. Archived and active endpoints are both listed."
            : "Archiving stops the endpoint's checks and hides it from active lists.",
        "Endpoint",
        ["Client", "Website", "Environment"],
        endpoints.Select(endpoint => new RegistryActionRow(
            endpoint.Id,
            endpoint.Version,
            endpoint.DisplayUrl,
            null,
            [endpoint.ClientName, endpoint.WebsiteName, endpoint.EnvironmentName],
            endpoint.IsDeleted ? "Archived" : endpoint.IsEnabled ? "Enabled" : "Disabled",
            RegistryActionScreens.Tone(endpoint.IsDeleted, endpoint.IsEnabled))).ToArray(),
        destructive ? nameof(PurgeEndpoint) : nameof(ArchiveEndpoint),
        nameof(Endpoint),
        destructive ? "Delete permanently" : "Archive",
        destructive ? "trash" : "archive",
        destructive,
        destructive
            ? "Permanently delete {0} and all of its checks, results, SEO observations, crawls, incidents and notifications? This cannot be undone."
            : "Archive {0}? Its scheduled checks stop and it moves to the archive.",
        destructive ? "No endpoints to delete" : "No endpoints to archive",
        "No endpoint records are available within your current access scope.",
        nameof(Endpoints),
        "Back to endpoints");

    private async Task<IActionResult> RunScreenActionAsync(
        Guid id,
        long version,
        Func<RegistryVersionCommand, RegistryAccessContext, CancellationToken, Task<RegistryMutationResult>> operation,
        string screenAction,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await operation(new(id, version), GetAccess(), cancellationToken);
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord("endpoint");
        }

        TempData.AddFlashMessage(
            result.Succeeded ? FlashLevel.Success : FlashLevel.Error,
            result.Succeeded ? successMessage : string.Join(" ", result.Errors));
        return RedirectToAction(screenAction);
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }

    private static bool CanManage(RegistryAccessContext access) => RegistryVisibilityRoleNames.Any(access.Roles.Contains);
    private static readonly string[] RegistryVisibilityRoleNames = [ApplicationRoles.Administrator, ApplicationRoles.Operations];

    private async Task<EndpointRegistrationFormViewModel> BuildRegistrationFormAsync(
        EndpointRegistrationFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = GetAccess();
        model.Clients = (await registryReader.ListClientsAsync(access, cancellationToken))
            .Where(client => !client.IsDeleted)
            .ToArray();
        model.Websites = (await registryReader.ListWebsitesAsync(access, cancellationToken: cancellationToken))
            .Where(website => !website.IsDeleted)
            .ToArray();
        model.Environments = (await targetReader.ListAllEnvironmentsAsync(access, cancellationToken))
            .Where(environment => !environment.IsDeleted)
            .ToArray();
        var owners = (await registryReader.ListOwnersAsync(cancellationToken: cancellationToken)).ToList();
        var selectedOwnerIds = new[]
        {
            model.ClientOwnerSubjectId,
            model.WebsiteOwnerSubjectId,
            model.OwnerSubjectId
        }.OfType<Guid>().Distinct();
        foreach (var selectedOwnerId in selectedOwnerIds.Where(selectedOwnerId =>
                     owners.All(owner => owner.OwnerSubjectId != selectedOwnerId)))
        {
            owners.AddRange((await registryReader.ListOwnersAsync(selectedOwnerId, cancellationToken))
                .Where(owner => owners.All(existing => existing.OwnerSubjectId != owner.OwnerSubjectId)));
        }
        model.Owners = owners.OrderBy(owner => owner.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        model.CanApproveHttp = User.IsInRole(ApplicationRoles.Administrator);
        model.CanConfigureInterval = User.IsInRole(ApplicationRoles.Administrator);
        model.AdvancedSettingsOpen = model.AdvancedSettingsOpen || AdvancedRegistrationFields.Any(field =>
            ModelState.TryGetValue(field, out var entry) && entry.Errors.Count > 0);
        return model;
    }

    private static readonly string[] AdvancedRegistrationFields =
    [
        nameof(EndpointRegistrationFormViewModel.OwnerSubjectId),
        nameof(EndpointRegistrationFormViewModel.TargetAuthorizationExpiresAt),
        nameof(EndpointRegistrationFormViewModel.SchedulingEnabled),
        nameof(EndpointRegistrationFormViewModel.IntervalMinutesOverride),
        nameof(EndpointRegistrationFormViewModel.WarningThresholdMsOverride),
        nameof(EndpointRegistrationFormViewModel.CriticalThresholdMsOverride),
        nameof(EndpointRegistrationFormViewModel.SeoExpectedCanonicalHost),
        nameof(EndpointRegistrationFormViewModel.SeoIndexingExpectation),
        nameof(EndpointRegistrationFormViewModel.SeoDescriptionRequired),
        nameof(EndpointRegistrationFormViewModel.PageAuditEnabled),
        nameof(EndpointRegistrationFormViewModel.PageAuditSchedulingEnabled),
        nameof(EndpointRegistrationFormViewModel.PageAuditIntervalHours),
        nameof(EndpointRegistrationFormViewModel.HttpExceptionReason)
    ];

    private static void SelectDefaultRegistrationMode(EndpointRegistrationFormViewModel model)
    {
        if (model.Environments.Count > 0)
        {
            model.HierarchyMode = EndpointRegistrationModes.ExistingEnvironment;
            return;
        }

        if (model.Websites.Count > 0)
        {
            model.HierarchyMode = EndpointRegistrationModes.NewEnvironment;
            return;
        }

        model.HierarchyMode = model.Clients.Count > 0
            ? EndpointRegistrationModes.NewWebsite
            : EndpointRegistrationModes.NewClient;
    }

    private static EndpointRegistrationHierarchy BuildRegistrationHierarchy(
        EndpointRegistrationFormViewModel model) => model.HierarchyMode switch
        {
            EndpointRegistrationModes.ExistingEnvironment =>
                new ExistingEnvironment(Required(model.EnvironmentId)),
            EndpointRegistrationModes.NewEnvironment =>
                new NewEnvironment(Required(model.WebsiteId), BuildEnvironment(model)),
            EndpointRegistrationModes.NewWebsite =>
                new NewWebsite(
                    Required(model.ClientId),
                    BuildWebsite(model),
                    BuildEnvironment(model)),
            EndpointRegistrationModes.NewClient =>
                new NewClient(
                    new EndpointRegistrationClient(
                        model.ClientName,
                        Required(model.ClientOwnerSubjectId),
                        model.ClientNotes),
                    BuildWebsite(model),
                    BuildEnvironment(model)),
            _ => throw new InvalidOperationException("Unknown endpoint registration mode.")
        };

    private static EndpointRegistrationWebsite BuildWebsite(
        EndpointRegistrationFormViewModel model) => new(
        model.WebsiteName,
        Required(model.WebsiteOwnerSubjectId),
        model.WebsiteTechnologyCms,
        TagNormalizer.Split(model.WebsiteTags));

    private static EndpointRegistrationEnvironment BuildEnvironment(
        EndpointRegistrationFormViewModel model) => new(
        model.EnvironmentName,
        model.EnvironmentType,
        model.EnvironmentBaseUrl);

    private static EndpointRegistrationSettings BuildRegistrationSettings(
        EndpointRegistrationFormViewModel model) => new()
        {
            Url = model.Url,
            OwnerSubjectId = model.OwnerSubjectId,
            IsEnabled = model.IsEnabled,
            HttpExceptionReason = model.HttpExceptionReason,
            TargetAuthorizationKind = model.TargetAuthorizationKind,
            TargetAuthorizationEvidence = model.TargetAuthorizationEvidence,
            TargetAuthorizationExpiresAt = model.TargetAuthorizationExpiresAt,
            IntervalMinutesOverride = model.IntervalMinutesOverride,
            SchedulingEnabled = model.SchedulingEnabled,
            WarningThresholdMsOverride = model.WarningThresholdMsOverride,
            CriticalThresholdMsOverride = model.CriticalThresholdMsOverride,
            SeoExpectedCanonicalHost = model.SeoExpectedCanonicalHost,
            SeoIndexingExpectation = model.SeoIndexingExpectation,
            SeoDescriptionRequired = model.SeoDescriptionRequired,
            PageAuditEnabled = model.PageAuditEnabled,
            PageAuditSchedulingEnabled = model.PageAuditSchedulingEnabled,
            PageAuditIntervalHours = model.PageAuditIntervalHours
        };

    private static Guid Required(Guid? value) =>
        value ?? throw new InvalidOperationException("A validated registration field is missing.");

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

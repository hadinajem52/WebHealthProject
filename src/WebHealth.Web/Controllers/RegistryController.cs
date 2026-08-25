using WebHealth.Application;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Domain.Normalization;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;
using WebHealth.Web.Ajax;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class RegistryController(
    IRegistryReader registryReader,
    IClientRegistryService clientService,
    IWebsiteRegistryService websiteService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Clients(CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new RegistryListViewModel(
            await registryReader.ListClientsAsync(access, cancellationToken),
            [],
            RegistryCanManage(access),
            [],
            null));
    }

    [HttpGet]
    public async Task<IActionResult> Websites(Guid? tagId, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new RegistryListViewModel(
            [],
            await registryReader.ListWebsitesAsync(access, tagId, cancellationToken),
            RegistryCanManage(access),
            await registryReader.ListTagsAsync(access, cancellationToken),
            tagId));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> Archived(CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(new RegistryArchiveViewModel(
            await registryReader.ListDeletedClientsAsync(access, cancellationToken),
            await registryReader.ListDeletedWebsitesAsync(access, cancellationToken),
            User.IsInRole(ApplicationRoles.Administrator)));
    }

    [HttpGet]
    public async Task<IActionResult> Client(Guid id, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        var client = await registryReader.FindClientAsync(id, access, cancellationToken);
        return client is null
            ? this.NotFoundRecord("client")
            : View(new ClientDetailsViewModel(client, RegistryCanManage(access)));
    }

    [HttpGet]
    public async Task<IActionResult> Website(Guid id, CancellationToken cancellationToken)
    {
        var access = GetAccess();
        var website = await registryReader.FindWebsiteAsync(id, access, cancellationToken);
        return website is null
            ? this.NotFoundRecord("website")
            : View(new WebsiteDetailsViewModel(website, RegistryCanManage(access)));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> CreateClient(CancellationToken cancellationToken) =>
        View(await BuildClientFormAsync(new ClientFormViewModel(), cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> CreateClient(
        ClientFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(CreateClient),
                await BuildClientFormAsync(model, cancellationToken));
        }

        var result = await clientService.CreateAsync(
            new CreateClient(model.Name, model.OwnerSubjectId!.Value, model.Notes),
            GetAccess(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return this.ValidationView(
                nameof(CreateClient),
                await BuildClientFormAsync(model, cancellationToken));
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Client), new { id = result.EntityId })!,
            "Client created successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> EditClient(Guid id, CancellationToken cancellationToken)
    {
        var client = await registryReader.FindClientAsync(id, GetAccess(), cancellationToken);
        if (client is null)
        {
            return this.NotFoundRecord("client");
        }

        return View(await BuildClientFormAsync(new ClientFormViewModel
        {
            ClientId = client.Id,
            Name = client.Name,
            OwnerSubjectId = client.OwnerSubjectId,
            Notes = client.Notes,
            IsActive = client.IsActive,
            Version = client.Version
        }, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> EditClient(
        ClientFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(EditClient),
                await BuildClientFormAsync(model, cancellationToken));
        }

        var result = await clientService.UpdateAsync(
            new UpdateClient(
                model.ClientId,
                model.Name,
                model.OwnerSubjectId!.Value,
                model.Notes,
                model.IsActive,
                model.Version),
            GetAccess(),
            cancellationToken);
        if (!result.Succeeded)
        {
            return await HandleClientEditFailureAsync(model, result, cancellationToken);
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Client), new { id = model.ClientId })!,
            "Client updated successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DisableClient(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeClientStateAsync(id, version, clientService.DisableAsync, "Client disabled.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DeleteClient(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeClientStateAsync(id, version, clientService.DeleteAsync, "Client archived.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> RestoreClient(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeClientStateAsync(id, version, clientService.RestoreAsync, "Client restored in a disabled state.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> CreateWebsite(Guid? clientId, CancellationToken cancellationToken) =>
        View(await BuildWebsiteFormAsync(
            new WebsiteFormViewModel { ClientId = clientId },
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> CreateWebsite(
        WebsiteFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(CreateWebsite),
                await BuildWebsiteFormAsync(model, cancellationToken));
        }

        var result = await websiteService.CreateAsync(
            new CreateWebsite(
                model.ClientId!.Value,
                model.Name,
                model.OwnerSubjectId!.Value,
                model.TechnologyCms,
                model.IsEnabled,
                TagNormalizer.Split(model.Tags)),
            GetAccess(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return this.ValidationView(
                nameof(CreateWebsite),
                await BuildWebsiteFormAsync(model, cancellationToken));
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Website), new { id = result.EntityId })!,
            "Website created successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> EditWebsite(Guid id, CancellationToken cancellationToken)
    {
        var website = await registryReader.FindWebsiteAsync(id, GetAccess(), cancellationToken);
        if (website is null)
        {
            return this.NotFoundRecord("website");
        }

        return View(await BuildWebsiteFormAsync(new WebsiteFormViewModel
        {
            WebsiteId = website.Id,
            ClientId = website.ClientId,
            Name = website.Name,
            OwnerSubjectId = website.OwnerSubjectId,
            TechnologyCms = website.TechnologyCms,
            Tags = string.Join(", ", website.Tags),
            IsEnabled = website.IsEnabled,
            Version = website.Version
        }, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public async Task<IActionResult> EditWebsite(
        WebsiteFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return this.ValidationView(
                nameof(EditWebsite),
                await BuildWebsiteFormAsync(model, cancellationToken));
        }

        var result = await websiteService.UpdateAsync(
            new UpdateWebsite(
                model.WebsiteId,
                model.Name,
                model.OwnerSubjectId!.Value,
                model.TechnologyCms,
                model.IsEnabled,
                model.Version,
                TagNormalizer.Split(model.Tags)),
            GetAccess(),
            cancellationToken);
        if (!result.Succeeded)
        {
            return await HandleWebsiteEditFailureAsync(model, result, cancellationToken);
        }

        return this.RedirectOrAjaxNavigate(
            Url.Action(nameof(Website), new { id = model.WebsiteId })!,
            "Website updated successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DisableWebsite(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeWebsiteStateAsync(id, version, websiteService.DisableAsync, "Website disabled.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> DeleteWebsite(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeWebsiteStateAsync(id, version, websiteService.DeleteAsync, "Website archived.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> RestoreWebsite(Guid id, long version, CancellationToken cancellationToken) =>
        ChangeWebsiteStateAsync(id, version, websiteService.RestoreAsync, "Website restored in a disabled state.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpPost]
    public async Task<IActionResult> PurgeWebsite(Guid id, long version, CancellationToken cancellationToken)
    {
        // Unlike the other lifecycle actions this cannot fall back to the website's own page,
        // which no longer exists once the purge succeeds.
        var result = await websiteService.PurgeAsync(new(id, version), GetAccess(), cancellationToken);
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord("website");
        }

        TempData.AddFlashMessage(
            result.Succeeded ? FlashLevel.Success : FlashLevel.Error,
            result.Succeeded
                ? "Website permanently deleted with its environments, endpoints and monitoring history."
                : string.Join(" ", result.Errors));
        return RedirectToAction(nameof(Archived));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        var roles = ApplicationRoles.All
            .Select(role => role.Name)
            .Where(User.IsInRole)
            .ToArray();
        return new(userId, roles);
    }

    private static bool RegistryCanManage(RegistryAccessContext access) =>
        access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal)
        || access.Roles.Contains(ApplicationRoles.Operations, StringComparer.Ordinal);

    private async Task<ClientFormViewModel> BuildClientFormAsync(
        ClientFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Owners = await registryReader.ListOwnersAsync(model.OwnerSubjectId, cancellationToken);
        return model;
    }

    private async Task<WebsiteFormViewModel> BuildWebsiteFormAsync(
        WebsiteFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Owners = await registryReader.ListOwnersAsync(model.OwnerSubjectId, cancellationToken);
        model.Clients = await registryReader.ListClientsAsync(GetAccess(), cancellationToken);
        return model;
    }

    private async Task<IActionResult> HandleClientEditFailureAsync(
        ClientFormViewModel model,
        RegistryMutationResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord("client");
        }

        AddErrors(result.Errors);
        return this.ValidationView(
            nameof(EditClient),
            await BuildClientFormAsync(model, cancellationToken),
            result.Status == RegistryMutationStatus.ConcurrencyConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity);
    }

    private async Task<IActionResult> HandleWebsiteEditFailureAsync(
        WebsiteFormViewModel model,
        RegistryMutationResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord("website");
        }

        AddErrors(result.Errors);
        return this.ValidationView(
            nameof(EditWebsite),
            await BuildWebsiteFormAsync(model, cancellationToken),
            result.Status == RegistryMutationStatus.ConcurrencyConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity);
    }

    private async Task<IActionResult> ChangeClientStateAsync(
        Guid id,
        long version,
        Func<RegistryVersionCommand, RegistryAccessContext, CancellationToken, Task<RegistryMutationResult>> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await operation(new(id, version), GetAccess(), cancellationToken);
        return FinishStateChange(result, "client", nameof(Clients), nameof(Client), id, successMessage);
    }

    private async Task<IActionResult> ChangeWebsiteStateAsync(
        Guid id,
        long version,
        Func<RegistryVersionCommand, RegistryAccessContext, CancellationToken, Task<RegistryMutationResult>> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await operation(new(id, version), GetAccess(), cancellationToken);
        return FinishStateChange(result, "website", nameof(Websites), nameof(Website), id, successMessage);
    }

    private IActionResult FinishStateChange(
        RegistryMutationResult result,
        string noun,
        string listAction,
        string detailsAction,
        Guid id,
        string successMessage)
    {
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord(noun);
        }

        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors);
            if (Request.IsWebHealthAjax())
            {
                return StatusCode(
                    result.Status == RegistryMutationStatus.ConcurrencyConflict
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status422UnprocessableEntity,
                    new AjaxFragmentViewModel(
                        message,
                        "error",
                        RefreshUrl: Url.Action(detailsAction, new { id })));
            }
            TempData.AddFlashMessage(FlashLevel.Error, message);
            return RedirectToAction(detailsAction, new { id });
        }

        if (Request.IsWebHealthAjax())
        {
            return Ok(new AjaxFragmentViewModel(
                successMessage,
                "success",
                RefreshUrl: Url.Action(detailsAction, new { id })));
        }
        TempData.AddFlashMessage(FlashLevel.Success, successMessage);
        return RedirectToAction(listAction);
    }

    private void AddErrors(IEnumerable<ValidationError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message);
        }
    }
}

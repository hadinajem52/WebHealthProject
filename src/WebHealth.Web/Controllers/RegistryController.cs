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
            null,
            User.IsInRole(ApplicationRoles.Administrator)));
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
            tagId,
            User.IsInRole(ApplicationRoles.Administrator)));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> ArchiveClients(CancellationToken cancellationToken) =>
        View(RegistryActionScreens.ViewName, BuildClientScreen(
            await registryReader.ListClientsAsync(GetAccess(), cancellationToken),
            destructive: false));

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpGet]
    public async Task<IActionResult> DeleteClients(CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(RegistryActionScreens.ViewName, BuildClientScreen(
            RegistryActionScreens.Merge(
                await registryReader.ListClientsAsync(access, cancellationToken),
                await registryReader.ListDeletedClientsAsync(access, cancellationToken),
                client => client.Name),
            destructive: true));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> ArchiveWebsites(CancellationToken cancellationToken) =>
        View(RegistryActionScreens.ViewName, BuildWebsiteScreen(
            await registryReader.ListWebsitesAsync(GetAccess(), cancellationToken: cancellationToken),
            destructive: false));

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpGet]
    public async Task<IActionResult> DeleteWebsites(CancellationToken cancellationToken)
    {
        var access = GetAccess();
        return View(RegistryActionScreens.ViewName, BuildWebsiteScreen(
            RegistryActionScreens.Merge(
                await registryReader.ListWebsitesAsync(access, cancellationToken: cancellationToken),
                await registryReader.ListDeletedWebsitesAsync(access, cancellationToken),
                website => $"{website.ClientName} {website.Name}"),
            destructive: true));
    }

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> ArchivedClients(CancellationToken cancellationToken) =>
        View(RegistryArchiveScreens.ViewName, BuildClientArchiveScreen(
            await registryReader.ListDeletedClientsAsync(GetAccess(), cancellationToken)));

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpGet]
    public async Task<IActionResult> ArchivedWebsites(CancellationToken cancellationToken) =>
        View(RegistryArchiveScreens.ViewName, BuildWebsiteArchiveScreen(
            await registryReader.ListDeletedWebsitesAsync(GetAccess(), cancellationToken)));

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

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> ArchiveClient(Guid id, long version, CancellationToken cancellationToken) =>
        RunScreenActionAsync(id, version, clientService.DeleteAsync, "client", nameof(ArchiveClients),
            "Client archived with every website, environment and endpoint beneath it.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpPost]
    public Task<IActionResult> PurgeClient(Guid id, long version, CancellationToken cancellationToken) =>
        RunScreenActionAsync(id, version, clientService.PurgeAsync, "client", nameof(DeleteClients),
            "Client permanently deleted with every website, environment, endpoint and monitoring record beneath it.",
            cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ManageRegistry), HttpPost]
    public Task<IActionResult> ArchiveWebsite(Guid id, long version, CancellationToken cancellationToken) =>
        RunScreenActionAsync(id, version, websiteService.DeleteAsync, "website", nameof(ArchiveWebsites),
            "Website archived with every environment and endpoint beneath it.", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.Administration), HttpPost]
    public Task<IActionResult> PurgeWebsite(Guid id, long version, CancellationToken cancellationToken) =>
        RunScreenActionAsync(id, version, websiteService.PurgeAsync, "website", nameof(DeleteWebsites),
            "Website permanently deleted with every environment, endpoint and monitoring record beneath it.",
            cancellationToken);

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

    private static RegistryArchiveScreenViewModel BuildClientArchiveScreen(
        IReadOnlyList<ClientListItem> clients) => new(
        "Archived clients",
        "Archived clients keep their websites, environments, endpoints and audit history. Restoring one brings it back disabled so you decide when monitoring resumes.",
        "Client",
        ["Owner"],
        clients.Select(client => new RegistryArchiveRow(
            client.Id,
            client.Version,
            client.Name,
            $"Version {client.Version}",
            [client.OwnerName])).ToArray(),
        nameof(Client),
        nameof(RestoreClient),
        "Restore {0}? It comes back disabled and nothing beneath it is checked until you enable it.",
        "client",
        "No archived clients",
        "Archiving a client from the client registry moves it here.",
        nameof(Clients),
        "Back to clients");

    private static RegistryArchiveScreenViewModel BuildWebsiteArchiveScreen(
        IReadOnlyList<WebsiteListItem> websites) => new(
        "Archived websites",
        "Archived websites keep their environments, endpoints and audit history. Restoring one brings it back disabled so you decide when monitoring resumes.",
        "Website",
        ["Client", "Owner"],
        websites.Select(website => new RegistryArchiveRow(
            website.Id,
            website.Version,
            website.Name,
            website.TechnologyCms ?? "Technology not set",
            [website.ClientName, website.OwnerName])).ToArray(),
        nameof(Website),
        nameof(RestoreWebsite),
        "Restore {0}? It comes back disabled and nothing beneath it is checked until you enable it.",
        "website",
        "No archived websites",
        "Archiving a website from the website registry moves it here.",
        nameof(Websites),
        "Back to websites");

    private static RegistryActionScreenViewModel BuildClientScreen(
        IReadOnlyList<ClientListItem> clients,
        bool destructive) => new(
        destructive ? "Delete clients" : "Archive clients",
        destructive
            ? "Permanent deletion removes the client with every website, environment, endpoint and monitoring record beneath it. Archived and active clients are both listed."
            : "Archiving hides the client from active lists and archives every website, environment and endpoint beneath it.",
        "Client",
        ["Owner", "Websites"],
        clients.Select(client => new RegistryActionRow(
            client.Id,
            client.Version,
            client.Name,
            $"Version {client.Version}",
            [client.OwnerName, $"{client.VisibleWebsiteCount}"],
            client.IsDeleted ? "Archived" : client.IsActive ? "Active" : "Disabled",
            RegistryActionScreens.Tone(client.IsDeleted, client.IsActive))).ToArray(),
        destructive ? nameof(PurgeClient) : nameof(ArchiveClient),
        nameof(Client),
        destructive ? "Delete permanently" : "Archive",
        destructive ? "trash" : "archive",
        destructive,
        destructive
            ? "Permanently delete {0} with every website, environment, endpoint and monitoring record beneath it? This cannot be undone."
            : "Archive {0}? Every website, environment and endpoint beneath it is archived too and monitoring stops.",
        destructive ? "No clients to delete" : "No clients to archive",
        "No client records are available within your current access scope.",
        nameof(Clients),
        "Back to clients");

    private static RegistryActionScreenViewModel BuildWebsiteScreen(
        IReadOnlyList<WebsiteListItem> websites,
        bool destructive) => new(
        destructive ? "Delete websites" : "Archive websites",
        destructive
            ? "Permanent deletion removes the website with every environment, endpoint and monitoring record beneath it. Archived and active websites are both listed."
            : "Archiving hides the website from active lists and archives every environment and endpoint beneath it.",
        "Website",
        ["Client", "Owner", "Environments"],
        websites.Select(website => new RegistryActionRow(
            website.Id,
            website.Version,
            website.Name,
            website.TechnologyCms ?? "Technology not set",
            [website.ClientName, website.OwnerName, $"{website.ActiveEnvironmentCount} active"],
            website.IsDeleted ? "Archived" : website.IsEnabled ? "Enabled" : "Disabled",
            RegistryActionScreens.Tone(website.IsDeleted, website.IsEnabled))).ToArray(),
        destructive ? nameof(PurgeWebsite) : nameof(ArchiveWebsite),
        nameof(Website),
        destructive ? "Delete permanently" : "Archive",
        destructive ? "trash" : "archive",
        destructive,
        destructive
            ? "Permanently delete {0} with every environment, endpoint and monitoring record beneath it? This cannot be undone."
            : "Archive {0}? Every environment and endpoint beneath it is archived too and monitoring stops.",
        destructive ? "No websites to delete" : "No websites to archive",
        "No website records are available within your current access scope.",
        nameof(Websites),
        "Back to websites");

    private async Task<IActionResult> RunScreenActionAsync(
        Guid id,
        long version,
        Func<RegistryVersionCommand, RegistryAccessContext, CancellationToken, Task<RegistryMutationResult>> operation,
        string noun,
        string screenAction,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await operation(new(id, version), GetAccess(), cancellationToken);
        if (result.Status == RegistryMutationStatus.NotFound)
        {
            return this.NotFoundRecord(noun);
        }

        TempData.AddFlashMessage(
            result.Succeeded ? FlashLevel.Success : FlashLevel.Error,
            result.Succeeded ? successMessage : string.Join(" ", result.Errors));
        return RedirectToAction(screenAction);
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

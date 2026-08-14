using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Controllers;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class RegistryConcurrencyResponseTests
{
    [Fact]
    public async Task ClientConflict_DoesNotReplaceSubmittedVersion()
    {
        var controller = CreateController();
        var model = new ClientFormViewModel
        {
            ClientId = Guid.NewGuid(),
            Name = "Stale client edit",
            OwnerSubjectId = Guid.NewGuid(),
            Version = 7
        };

        var result = await controller.EditClient(model, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        model.Version.Should().Be(7);
        controller.ModelState.Should().NotBeEmpty();
    }

    [Fact]
    public async Task WebsiteConflict_DoesNotReplaceSubmittedVersion()
    {
        var controller = CreateController();
        var model = new WebsiteFormViewModel
        {
            WebsiteId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "Stale website edit",
            OwnerSubjectId = Guid.NewGuid(),
            Version = 11
        };

        var result = await controller.EditWebsite(model, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        model.Version.Should().Be(11);
        controller.ModelState.Should().NotBeEmpty();
    }

    private static RegistryController CreateController()
    {
        var services = new ConflictingRegistryServices();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, ApplicationRoles.Administrator)
        ], "Test");
        return new RegistryController(services, services, services)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class ConflictingRegistryServices :
        IRegistryReader,
        IClientRegistryService,
        IWebsiteRegistryService
    {
        private static readonly RegistryMutationResult Conflict = RegistryMutationResult.Failure(
            RegistryMutationStatus.ConcurrencyConflict,
            "The record changed. Reload and try again.");

        public Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ClientListItem>>([]);

        public Task<IReadOnlyList<ClientListItem>> ListDeletedClientsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ClientListItem>>([]);

        public Task<ClientDetails?> FindClientAsync(
            Guid clientId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<ClientDetails?>(null);

        public Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
            RegistryAccessContext access,
            Guid? tagId = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WebsiteListItem>>([]);

        public Task<IReadOnlyList<RegistryTagOption>> ListTagsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RegistryTagOption>>([]);

        public Task<IReadOnlyList<WebsiteListItem>> ListDeletedWebsitesAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WebsiteListItem>>([]);

        public Task<WebsiteDetails?> FindWebsiteAsync(
            Guid websiteId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<WebsiteDetails?>(null);

        public Task<IReadOnlyList<RegistryOwnerOption>> ListOwnersAsync(
            Guid? includeOwnerSubjectId = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RegistryOwnerOption>>([]);

        Task<RegistryMutationResult> IClientRegistryService.CreateAsync(
            CreateClient command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IClientRegistryService.UpdateAsync(
            UpdateClient command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IClientRegistryService.DisableAsync(
            RegistryVersionCommand command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IClientRegistryService.DeleteAsync(
            RegistryVersionCommand command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IClientRegistryService.RestoreAsync(
            RegistryVersionCommand command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IWebsiteRegistryService.CreateAsync(
            CreateWebsite command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IWebsiteRegistryService.UpdateAsync(
            UpdateWebsite command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IWebsiteRegistryService.DisableAsync(
            RegistryVersionCommand command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IWebsiteRegistryService.DeleteAsync(
            RegistryVersionCommand command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);

        Task<RegistryMutationResult> IWebsiteRegistryService.RestoreAsync(
            RegistryVersionCommand command,
            RegistryAccessContext access,
            CancellationToken cancellationToken) => Task.FromResult(Conflict);
    }
}

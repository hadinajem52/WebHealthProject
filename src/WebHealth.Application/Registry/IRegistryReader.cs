namespace WebHealth.Application.Registry;

public interface IRegistryReader
{
    Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientListItem>> ListDeletedClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<ClientDetails?> FindClientAsync(
        Guid clientId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebsiteListItem>> ListDeletedWebsitesAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<WebsiteDetails?> FindWebsiteAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegistryOwnerOption>> ListOwnersAsync(
        Guid? includeOwnerSubjectId = null,
        CancellationToken cancellationToken = default);
}

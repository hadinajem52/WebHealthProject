using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record RegistryListViewModel(
    IReadOnlyList<ClientListItem> Clients,
    IReadOnlyList<WebsiteListItem> Websites,
    bool CanManage);

public sealed record ClientDetailsViewModel(ClientDetails Client, bool CanManage);

public sealed record WebsiteDetailsViewModel(WebsiteDetails Website, bool CanManage);

public sealed record RegistryArchiveViewModel(
    IReadOnlyList<ClientListItem> Clients,
    IReadOnlyList<WebsiteListItem> Websites);

public sealed class ClientFormViewModel
{
    public Guid ClientId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, Display(Name = "Owner")]
    public Guid? OwnerSubjectId { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [Display(Name = "Client active")]
    public bool IsActive { get; set; } = true;

    public long Version { get; set; }

    public IReadOnlyList<RegistryOwnerOption> Owners { get; set; } = [];
}

public sealed class WebsiteFormViewModel
{
    public Guid WebsiteId { get; set; }

    [Required, Display(Name = "Client")]
    public Guid? ClientId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, Display(Name = "Owner")]
    public Guid? OwnerSubjectId { get; set; }

    [StringLength(200), Display(Name = "Technology / CMS")]
    public string? TechnologyCms { get; set; }

    [Display(Name = "Website enabled")]
    public bool IsEnabled { get; set; }

    public long Version { get; set; }

    public IReadOnlyList<RegistryOwnerOption> Owners { get; set; } = [];

    public IReadOnlyList<ClientListItem> Clients { get; set; } = [];
}

using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record RegistryListViewModel(
    IReadOnlyList<ClientListItem> Clients,
    IReadOnlyList<WebsiteListItem> Websites,
    bool CanManage,
    IReadOnlyList<RegistryTagOption> Tags,
    Guid? SelectedTagId);

public sealed record ClientDetailsViewModel(ClientDetails Client, bool CanManage);

public sealed record WebsiteDetailsViewModel(WebsiteDetails Website, bool CanManage);

public sealed record RegistryArchiveViewModel(
    IReadOnlyList<ClientListItem> Clients,
    IReadOnlyList<WebsiteListItem> Websites,
    bool CanPurge);

public sealed class ClientFormViewModel
{
    public Guid ClientId { get; set; }

    [Required(ErrorMessage = "Enter a client name.")]
    [StringLength(200, ErrorMessage = "This name is too long. Use 200 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select an owner. Every record needs someone answerable for it.")]
    [Display(Name = "Owner")]
    public Guid? OwnerSubjectId { get; set; }

    [StringLength(2000, ErrorMessage = "These notes are too long. Use 2000 characters or fewer.")]
    public string? Notes { get; set; }

    [Display(Name = "Client active")]
    public bool IsActive { get; set; } = true;

    public long Version { get; set; }

    public IReadOnlyList<RegistryOwnerOption> Owners { get; set; } = [];
}

public sealed class WebsiteFormViewModel
{
    public Guid WebsiteId { get; set; }

    [Required(ErrorMessage = "Select the client this website belongs to.")]
    [Display(Name = "Client")]
    public Guid? ClientId { get; set; }

    [Required(ErrorMessage = "Enter a website name.")]
    [StringLength(200, ErrorMessage = "This name is too long. Use 200 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select an owner. Every record needs someone answerable for it.")]
    [Display(Name = "Owner")]
    public Guid? OwnerSubjectId { get; set; }

    [StringLength(200, ErrorMessage = "This value is too long. Use 200 characters or fewer.")]
    [Display(Name = "Technology / CMS")]
    public string? TechnologyCms { get; set; }

    [StringLength(2020,
        ErrorMessage = "There are too many tags here. Use at most 20 tags of 100 characters each.")]
    [Display(Name = "Tags")]
    public string? Tags { get; set; }

    [Display(Name = "Website enabled")]
    public bool IsEnabled { get; set; }

    public long Version { get; set; }

    public IReadOnlyList<RegistryOwnerOption> Owners { get; set; } = [];

    public IReadOnlyList<ClientListItem> Clients { get; set; } = [];
}

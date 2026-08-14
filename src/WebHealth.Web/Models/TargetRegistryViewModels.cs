using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record EnvironmentListViewModel(
    Guid WebsiteId,
    string WebsiteName,
    IReadOnlyList<EnvironmentListItem> Environments,
    bool CanManage);

public sealed record EnvironmentDetailsViewModel(EnvironmentDetails Environment, bool CanManage);
public sealed record EndpointDetailsViewModel(EndpointDetails Endpoint, bool CanManage);
public sealed record TargetArchiveViewModel(
    IReadOnlyList<EnvironmentListItem> Environments,
    IReadOnlyList<EndpointListItem> Endpoints);

public sealed class EnvironmentFormViewModel
{
    public Guid EnvironmentId { get; set; }
    public Guid WebsiteId { get; set; }
    public string WebsiteName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, Display(Name = "Environment type")]
    public string EnvironmentType { get; set; } = EnvironmentTypes.Development;

    [StringLength(2048), Display(Name = "Base URL")]
    public string? BaseUrl { get; set; }

    [Display(Name = "Environment active")]
    public bool IsActive { get; set; } = true;

    public long Version { get; set; }
}

public sealed class EndpointFormViewModel
{
    public Guid EndpointId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public bool IsProduction { get; set; }
    public bool CanApproveHttp { get; set; }

    [Required, StringLength(2048), Display(Name = "Endpoint URL")]
    public string Url { get; set; } = string.Empty;

    [Display(Name = "Owner override")]
    public Guid? OwnerSubjectId { get; set; }

    [Display(Name = "Endpoint enabled")]
    public bool IsEnabled { get; set; } = true;

    [StringLength(500), Display(Name = "Production HTTP exception reason")]
    public string? HttpExceptionReason { get; set; }

    [Display(Name = "Target authorization")]
    public string? TargetAuthorizationKind { get; set; }

    [StringLength(500), Display(Name = "Ownership or permission reference")]
    public string? TargetAuthorizationEvidence { get; set; }

    [Display(Name = "Authorization expires")]
    public DateTimeOffset? TargetAuthorizationExpiresAt { get; set; }

    public long Version { get; set; }
    public IReadOnlyList<RegistryOwnerOption> Owners { get; set; } = [];
}

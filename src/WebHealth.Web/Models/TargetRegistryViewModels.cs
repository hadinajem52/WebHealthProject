using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Seo;
using WebHealth.Domain.PageAudits;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record EnvironmentListViewModel(
    Guid WebsiteId,
    string WebsiteName,
    IReadOnlyList<EnvironmentListItem> Environments,
    bool CanManage);

public sealed record EnvironmentDetailsViewModel(EnvironmentDetails Environment, bool CanManage);
public sealed record EndpointDetailsViewModel(
    EndpointDetails Endpoint,
    bool CanManage,
    bool CanPurge,
    CheckHistoryItem? LatestCheck,
    CertificateStatus Certificate);
public sealed record RegistryEndpointListViewModel(IReadOnlyList<RegistryEndpointItem> Endpoints, string? Search);
public sealed record TargetArchiveViewModel(
    IReadOnlyList<EnvironmentListItem> Environments,
    IReadOnlyList<EndpointListItem> Endpoints,
    bool CanPurge);

public sealed class EnvironmentFormViewModel
{
    public Guid EnvironmentId { get; set; }
    public Guid WebsiteId { get; set; }
    public string WebsiteName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an environment name, such as Production or Staging.")]
    [StringLength(100, ErrorMessage = "This name is too long. Use 100 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select an environment type. It decides the default check interval and the HTTPS rule.")]
    [Display(Name = "Environment type")]
    public string EnvironmentType { get; set; } = EnvironmentTypes.Development;

    [StringLength(2048, ErrorMessage = "This URL is too long. Use 2048 characters or fewer.")]
    [Display(Name = "Base URL")]
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
    public Guid WebsiteId { get; set; }
    public string WebsiteName { get; set; } = string.Empty;
    public bool IsProduction { get; set; }
    public bool CanApproveHttp { get; set; }

    [Required(ErrorMessage = "Enter the endpoint URL to watch, such as https://example.com/health.")]
    [StringLength(2048, ErrorMessage = "This URL is too long. Use 2048 characters or fewer.")]
    [Display(Name = "Endpoint URL")]
    public string Url { get; set; } = string.Empty;

    [Display(Name = "Owner override")]
    public Guid? OwnerSubjectId { get; set; }

    [Display(Name = "Endpoint enabled")]
    public bool IsEnabled { get; set; } = true;

    [Display(Name = "Run scheduled checks")]
    public bool SchedulingEnabled { get; set; } = true;

    [StringLength(500, ErrorMessage = "This reason is too long. Use 500 characters or fewer.")]
    [Display(Name = "Production HTTP exception reason")]
    public string? HttpExceptionReason { get; set; }

    [Display(Name = "Target authorization")]
    public string? TargetAuthorizationKind { get; set; }

    [StringLength(500, ErrorMessage = "This reference is too long. Use 500 characters or fewer.")]
    [Display(Name = "Ownership or permission reference")]
    public string? TargetAuthorizationEvidence { get; set; }

    [Display(Name = "Authorization expires")]
    public DateTimeOffset? TargetAuthorizationExpiresAt { get; set; }

    [Range(1, 1440,
        ErrorMessage = "Enter between 1 and 1440 minutes (24 hours), or leave blank for the default: "
            + "5 minutes for Production, 15 for everything else.")]
    [Display(Name = "Monitoring interval override (minutes)")]
    public int? IntervalMinutesOverride { get; set; }

    [Range(ResponseThresholdOverride.MinimumMs, ResponseThresholdOverride.MaximumMs,
        ErrorMessage = "Enter between {1} and {2} milliseconds, or leave blank for the 1,500 ms default.")]
    [Display(Name = "Slow-response warning threshold (ms)")]
    public int? WarningThresholdMsOverride { get; set; }

    [Range(ResponseThresholdOverride.MinimumMs, ResponseThresholdOverride.MaximumMs,
        ErrorMessage = "Enter between {1} and {2} milliseconds, or leave blank for the 3,000 ms default.")]
    [Display(Name = "Slow-response critical threshold (ms)")]
    public int? CriticalThresholdMsOverride { get; set; }

    [StringLength(253, ErrorMessage = "A host name cannot be longer than 253 characters.")]
    [Display(Name = "Expected canonical host")]
    public string? SeoExpectedCanonicalHost { get; set; }

    [Required(ErrorMessage = "Select an indexing expectation from the list.")]
    [Display(Name = "Indexing expectation")]
    public string SeoIndexingExpectation { get; set; } = SeoIndexingExpectations.Default;

    [Display(Name = "Require a meta description")]
    public bool SeoDescriptionRequired { get; set; } = true;

    [Display(Name = "Enable Google PageSpeed audits")]
    public bool PageAuditEnabled { get; set; }

    [Display(Name = "Run PageSpeed audits on a schedule")]
    public bool PageAuditSchedulingEnabled { get; set; }

    [Range(PageAuditCadence.MinimumIntervalHours, PageAuditCadence.MaximumIntervalHours,
        ErrorMessage = "Enter between {1} and {2} hours. Each run asks Google to load the page "
            + "eight times, so daily is usually enough.")]
    [Display(Name = "PageSpeed audit interval (hours)")]
    public int PageAuditIntervalHours { get; set; } = PageAuditCadence.DefaultIntervalHours;

    public bool CanConfigureInterval { get; set; }

    public long Version { get; set; }
    public IReadOnlyList<RegistryOwnerOption> Owners { get; set; } = [];
}

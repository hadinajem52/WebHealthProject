using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Monitoring;

namespace WebHealth.Web.Models;

public sealed class HttpPolicyFormViewModel
{
    [Range(1, 120)]
    [Display(Name = "Timeout override (seconds)")]
    public int? TimeoutSeconds { get; set; }

    [Range(1, 10)]
    [Display(Name = "Failure confirmation count")]
    public int? FailureConfirmationCount { get; set; }

    [Range(1, 10)]
    [Display(Name = "Recovery confirmation count")]
    public int? RecoveryConfirmationCount { get; set; }

    [StringLength(100)]
    [Display(Name = "Additional accepted status codes")]
    public string? AdditionalAcceptedStatusCodes { get; set; }

    [StringLength(500)]
    [Display(Name = "Required content marker")]
    public string? RequiredContentMarker { get; set; }

    [Display(Name = "Marker comparison")]
    public string? ContentMarkerComparison { get; set; }

    public HttpMonitorOverridesV2 ToOverrides() => new()
    {
        TimeoutSeconds = TimeoutSeconds,
        FailureConfirmationCount = FailureConfirmationCount,
        RecoveryConfirmationCount = RecoveryConfirmationCount,
        AdditionalAcceptedStatusCodes = string.IsNullOrWhiteSpace(AdditionalAcceptedStatusCodes) ? null
            : AdditionalAcceptedStatusCodes.Split(',').Select(value => int.TryParse(value.Trim(), out var status) ? status : -1).ToArray(),
        RequiredContentMarker = RequiredContentMarker,
        ContentMarkerComparison = string.IsNullOrEmpty(ContentMarkerComparison) ? null : ContentMarkerComparison
    };

    public static HttpPolicyFormViewModel From(HttpMonitorOverridesV2 policy) => new()
    {
        TimeoutSeconds = policy.TimeoutSeconds,
        FailureConfirmationCount = policy.FailureConfirmationCount,
        RecoveryConfirmationCount = policy.RecoveryConfirmationCount,
        AdditionalAcceptedStatusCodes = policy.AdditionalAcceptedStatusCodes is null ? null : string.Join(", ", policy.AdditionalAcceptedStatusCodes),
        RequiredContentMarker = policy.RequiredContentMarker,
        ContentMarkerComparison = policy.ContentMarkerComparison
    };
}

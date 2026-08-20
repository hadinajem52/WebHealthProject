using WebHealth.Application.Seo;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Registry;

public static class EnvironmentTypes
{
    public const string Production = "Production";
    public const string Staging = "Staging";
    public const string Preproduction = "Preproduction";
    public const string Test = "Test";
    public const string Development = "Development";
    public const string Custom = "Custom";

    public static IReadOnlyList<string> All { get; } =
        [Production, Staging, Preproduction, Test, Development, Custom];
}

public static class TargetAuthorizationKinds
{
    public const string Owned = "Owned";
    public const string ExplicitPermission = "ExplicitPermission";

    public static IReadOnlyList<string> All { get; } = [Owned, ExplicitPermission];
}

public sealed record EnvironmentListItem(
    Guid Id,
    Guid WebsiteId,
    string WebsiteName,
    string Name,
    string EnvironmentType,
    bool IsProduction,
    string? BaseUrl,
    bool IsActive,
    bool IsDeleted,
    long Version,
    int ActiveEndpointCount);

public sealed record EnvironmentDetails(
    Guid Id,
    Guid WebsiteId,
    string WebsiteName,
    string Name,
    string EnvironmentType,
    bool IsProduction,
    string? BaseUrl,
    bool IsActive,
    bool IsDeleted,
    long Version,
    IReadOnlyList<EndpointListItem> Endpoints);

public sealed record EndpointListItem(
    Guid Id,
    Guid EnvironmentId,
    string EnvironmentName,
    string WebsiteName,
    string DisplayUrl,
    string OwnerName,
    bool InheritsWebsiteOwner,
    bool IsEnabled,
    bool IsDeleted,
    long Version,
    string MonitorType);

public sealed record RegistryEndpointItem(
    Guid Id,
    Guid ClientId,
    string ClientName,
    Guid WebsiteId,
    string WebsiteName,
    Guid EnvironmentId,
    string EnvironmentName,
    string DisplayUrl,
    bool IsEnabled,
    bool CanTest,
    long Version,
    EndpointMonitoringMode MonitoringMode);

/// <summary>
/// How an endpoint is monitored, as three distinct states rather than a monitored/not flag. A
/// manual-only target is not a paused one: nobody paused it, and telling an operator it is paused
/// invites them to look for a resume button that does not apply.
/// </summary>
public enum EndpointMonitoringMode
{
    /// <summary>The endpoint or one of its owning records is disabled.</summary>
    Disabled,

    /// <summary>Scheduled checks are configured and running.</summary>
    Scheduled,

    /// <summary>Scheduled checks are configured but paused. Manual runs still work.</summary>
    Paused,

    /// <summary>No schedule was ever configured; the target runs on demand only.</summary>
    ManualOnly
}

public sealed record EndpointDetails(
    Guid Id,
    Guid EnvironmentId,
    string EnvironmentName,
    bool IsProduction,
    Guid WebsiteId,
    string WebsiteName,
    string DisplayUrl,
    string NormalizedUrl,
    short NormalizationVersion,
    Guid? OwnerSubjectId,
    string OwnerName,
    bool InheritsWebsiteOwner,
    bool IsEnabled,
    bool IsDeleted,
    bool HasHttpException,
    string? HttpExceptionReason,
    bool HasTargetAuthorization,
    string? TargetAuthorizationKind,
    string? TargetAuthorizationEvidence,
    DateTimeOffset? TargetAuthorizationExpiresAt,
    long Version,
    string MonitorType,
    int IntervalSeconds,
    int? IntervalMinutesOverride,
    int WarningThresholdMs,
    int CriticalThresholdMs,
    bool HasThresholdOverride,
    int TimeoutSeconds,
    bool MonitorEnabled,
    bool SchedulingEnabled,
    bool IsMonitoringEligible,
    bool CanTest,
    string? SeoExpectedCanonicalHost = null,
    string SeoIndexingExpectation = SeoIndexingExpectations.Default,
    bool SeoDescriptionRequired = true,
    bool PageAuditEnabled = false,
    bool PageAuditSchedulingEnabled = false,
    int PageAuditIntervalHours = 24);

/// <summary>
/// What the UI shows for an endpoint's certificate. <paramref name="IsMonitored" /> false means
/// Not Applicable rather than Unknown: an HTTP endpoint has nothing to inspect.
/// </summary>
public sealed record CertificateStatus(
    bool IsMonitored,
    CertificateObservationItem? Latest)
{
    public static readonly CertificateStatus NotApplicable = new(false, null);
}

public sealed record CertificateObservationItem(
    string Subject,
    string Issuer,
    string SerialNumber,
    string Sha256Fingerprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    int DaysRemaining,
    string ValidationCategory,
    bool HostnameMatched,
    bool ChainTrusted,
    string? SubjectAlternativeNames,
    DateTimeOffset ObservedAt,
    /// <summary>
    /// The BR-C04 expiry band for this observation, derived from the day count that was stored
    /// with it rather than from the current clock, so the page shows the same severity the
    /// check itself raised. <c>None</c> for a certificate that is not valid today: its
    /// validation category is the thing to report, not how soon it would have expired.
    /// </summary>
    CertificateExpirySeverity ExpirySeverity);

public sealed record CreateEnvironment(
    Guid WebsiteId,
    string Name,
    string EnvironmentType,
    string? BaseUrl,
    bool IsActive);

public sealed record UpdateEnvironment(
    Guid EnvironmentId,
    string Name,
    string EnvironmentType,
    string? BaseUrl,
    bool IsActive,
    long Version);

public sealed record CreateEndpoint(
    Guid EnvironmentId,
    string Url,
    Guid? OwnerSubjectId,
    bool IsEnabled,
    string? HttpExceptionReason,
    string? TargetAuthorizationKind,
    string? TargetAuthorizationEvidence,
    DateTimeOffset? TargetAuthorizationExpiresAt,
    int? IntervalMinutesOverride = null,
    bool SchedulingEnabled = true,
    int? WarningThresholdMsOverride = null,
    int? CriticalThresholdMsOverride = null,
    string? SeoExpectedCanonicalHost = null,
    string SeoIndexingExpectation = SeoIndexingExpectations.Default,
    bool SeoDescriptionRequired = true,
    bool PageAuditEnabled = false,
    bool PageAuditSchedulingEnabled = false,
    int PageAuditIntervalHours = 24);


public sealed record UpdateEndpoint(
    Guid EndpointId,
    string Url,
    Guid? OwnerSubjectId,
    bool IsEnabled,
    string? HttpExceptionReason,
    string? TargetAuthorizationKind,
    string? TargetAuthorizationEvidence,
    DateTimeOffset? TargetAuthorizationExpiresAt,
    long Version,
    int? IntervalMinutesOverride = null,
    bool SchedulingEnabled = true,
    int? WarningThresholdMsOverride = null,
    int? CriticalThresholdMsOverride = null,
    string? SeoExpectedCanonicalHost = null,
    string SeoIndexingExpectation = SeoIndexingExpectations.Default,
    bool SeoDescriptionRequired = true,
    bool PageAuditEnabled = false,
    bool PageAuditSchedulingEnabled = false,
    int PageAuditIntervalHours = 24);

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
    int TimeoutSeconds,
    bool MonitorEnabled,
    bool IsMonitoringEligible,
    bool CanTest);

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
    int? IntervalMinutesOverride = null);


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
    int? IntervalMinutesOverride = null);

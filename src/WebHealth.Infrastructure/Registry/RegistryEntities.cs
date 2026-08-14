namespace WebHealth.Infrastructure.Registry;

public sealed class Client
{
    public Guid Id { get; set; }
    public Guid OwnerSubjectId { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public short NormalizationVersion { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public long Version { get; set; }
    public ICollection<Website> Websites { get; } = [];
}

public sealed class Website
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid OwnerSubjectId { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public short NormalizationVersion { get; set; }
    public string? TechnologyCms { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public long Version { get; set; }
    public Client Client { get; set; } = null!;
    public ICollection<WebsiteEnvironment> Environments { get; } = [];
    public ICollection<WebsiteTag> WebsiteTags { get; } = [];
}

public sealed class Tag
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public short NormalizationVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public long Version { get; set; }
    public ICollection<WebsiteTag> WebsiteTags { get; } = [];
}

public sealed class WebsiteTag
{
    public Guid WebsiteId { get; set; }
    public Guid TagId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Website Website { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}

public sealed class WebsiteEnvironment
{
    public Guid Id { get; set; }
    public Guid WebsiteId { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public short NormalizationVersion { get; set; }
    public required string EnvironmentType { get; set; }
    public bool IsProduction { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public long Version { get; set; }
    public Website Website { get; set; } = null!;
    public ICollection<Endpoint> Endpoints { get; } = [];
}

public sealed class Endpoint
{
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid? OwnerSubjectId { get; set; }
    public required string DisplayUrl { get; set; }
    public required string NormalizedUrl { get; set; }
    public required byte[] NormalizedUrlHash { get; set; }
    public required string NormalizedHost { get; set; }
    public int EffectivePort { get; set; }
    public short NormalizationVersion { get; set; }
    public bool IsEnabled { get; set; }
    public string? HttpExceptionReason { get; set; }
    public Guid? HttpExceptionApprovedByUserId { get; set; }
    public DateTimeOffset? HttpExceptionApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public long Version { get; set; }
    public WebsiteEnvironment Environment { get; set; } = null!;
    public ICollection<EndpointMonitor> Monitors { get; } = [];
    public ICollection<TargetAuthorizationEvidence> TargetAuthorizations { get; } = [];
}

public sealed class TargetAuthorizationEvidence
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public required string AuthorizationKind { get; set; }
    public required string EvidenceReference { get; set; }
    public required string NormalizedHost { get; set; }
    public int Port { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public long Version { get; set; }
    public Endpoint Endpoint { get; set; } = null!;
}

public sealed class PolicyProfile
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string MonitorType { get; set; }
    public required string BoundedSettings { get; set; }
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long Version { get; set; }
}

public sealed class EndpointMonitor
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public Guid PolicyProfileId { get; set; }
    public required string MonitorType { get; set; }
    public required string BoundedOverrides { get; set; }
    public DateTimeOffset? ScheduleAnchor { get; set; }
    public DateTimeOffset? NextDueAt { get; set; }
    public required string ConfigurationFingerprint { get; set; }
    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
    public int FailureConfirmationCount { get; set; }
    public int RecoveryConfirmationCount { get; set; }
    public int? WarningThresholdMs { get; set; }
    public int? CriticalThresholdMs { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public long Version { get; set; }
    public Endpoint Endpoint { get; set; } = null!;
    public PolicyProfile PolicyProfile { get; set; } = null!;
}

public sealed class AccessGrant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string AccessLevel { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? WebsiteId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public Guid? EndpointId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}

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
}

public sealed class AccessGrant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string AccessLevel { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? WebsiteId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}

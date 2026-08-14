using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.Assignments;

public sealed class Team
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public short NormalizationVersion { get; set; }

    public bool IsDisabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedByUserId { get; set; }

    public long Version { get; set; }

    public ICollection<TeamMember> Members { get; } = [];
}

public sealed class TeamMember
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Team Team { get; set; } = null!;

    public ApplicationUser User { get; set; } = null!;
}

public sealed class OwnerSubject
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public Guid? TeamId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }
}

using WebHealth.Application.Registry;

namespace WebHealth.Application.Seo;

/// <summary>
/// BR-E07 and BR-E08 policy for one origin. The origin is the key because that is what a
/// <c>robots.txt</c> belongs to; an endpoint cannot own this setting without fifty endpoints on one
/// host owning fifty contradictory copies of it.
/// </summary>
public sealed record UpdateRobotsPolicy(
    string Origin,
    bool SitemapRequired,
    string? ConfiguredSitemapUrl,
    string? ExceptionReason,
    long Version);

public sealed record RobotsPolicyView(
    string Origin,
    string Status,
    bool SitemapRequired,
    string? ConfiguredSitemapUrl,
    bool SitemapAvailable,
    string? ExceptionReason,
    DateTimeOffset? ExceptionApprovedAt,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    long Version);

public interface IRobotsPolicyService
{
    Task<IReadOnlyList<RobotsPolicyView>> ListAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the operator's decision for an origin. An approved exception carries a reason and
    /// the approver's identity, so BR-E07 suppression is always attributable — never a silent flag.
    /// </summary>
    Task<RegistryMutationResult> UpdateAsync(
        UpdateRobotsPolicy command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

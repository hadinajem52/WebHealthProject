using WebHealth.Application.Registry;

namespace WebHealth.Application.Seo;

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

    Task<RegistryMutationResult> UpdateAsync(
        UpdateRobotsPolicy command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

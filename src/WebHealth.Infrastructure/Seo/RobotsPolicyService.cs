using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Seo;

/// <summary>
/// The authorized write path for origin-level SEO policy (BR-E07, BR-E08). Without it the
/// exception and sitemap fields would only be reachable by editing the database by hand, which is
/// not a policy decision anyone could audit.
/// </summary>
internal sealed class RobotsPolicyService(
    ApplicationDbContext dbContext,
    IAuditTrailWriter auditTrail,
    TimeProvider timeProvider) : IRobotsPolicyService
{
    public async Task<IReadOnlyList<RobotsPolicyView>> ListAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        return await dbContext.RobotsSnapshots.AsNoTracking()
            .OrderBy(snapshot => snapshot.Origin)
            .Select(snapshot => new RobotsPolicyView(
                snapshot.Origin,
                snapshot.Status,
                snapshot.SitemapRequired,
                snapshot.ConfiguredSitemapUrl,
                snapshot.SitemapAvailable,
                snapshot.ExceptionReason,
                snapshot.ExceptionApprovedAt,
                snapshot.FetchedAt,
                snapshot.ExpiresAt,
                snapshot.Version))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<RegistryMutationResult> UpdateAsync(
        UpdateRobotsPolicy command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!RegistryVisibility.CanManage(access))
        {
            return RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden);
        }

        if (Validate(command) is { } error)
        {
            return RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, error);
        }

        var snapshot = await dbContext.RobotsSnapshots
            .SingleOrDefaultAsync(item => item.Origin == command.Origin, cancellationToken);
        if (snapshot is null)
        {
            // Policy is set for an origin the registry already monitors; the refresh job creates
            // the row. Accepting policy for an unknown origin would invent a target.
            return RegistryMutationResult.Failure(
                RegistryMutationStatus.NotFound, "No monitored origin matches that value.");
        }

        var now = timeProvider.GetUtcNow();
        var before = ToAudit(snapshot);
        dbContext.Entry(snapshot).Property(item => item.Version).OriginalValue = command.Version;

        snapshot.SitemapRequired = command.SitemapRequired;
        snapshot.ConfiguredSitemapUrl = Trimmed(command.ConfiguredSitemapUrl);
        ApplyException(snapshot, Trimmed(command.ExceptionReason), access.UserId, now);
        snapshot.UpdatedAt = now;
        snapshot.Version++;

        try
        {
            await auditTrail.RecordRobotsPolicyMutationAsync(
                new(access.UserId, now), before, ToAudit(snapshot), cancellationToken);
            return RegistryMutationResult.Success(Guid.Empty);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return RegistryMutationResult.Failure(
                RegistryMutationStatus.ConcurrencyConflict,
                "This origin policy changed since it was loaded. Review it and try again.");
        }
    }

    /// <summary>
    /// Clearing the reason clears the approval with it. An exception that outlived its reason
    /// would be exactly the silent flag this design refuses to have.
    /// </summary>
    private static void ApplyException(
        RobotsSnapshot snapshot,
        string? reason,
        Guid actorId,
        DateTimeOffset now)
    {
        if (reason is null)
        {
            snapshot.ExceptionReason = null;
            snapshot.ExceptionApprovedByUserId = null;
            snapshot.ExceptionApprovedAt = null;
            return;
        }

        // Re-approving an unchanged reason keeps the original approval date: the decision did not
        // change, so its timestamp should not move.
        if (snapshot.ExceptionReason != reason || snapshot.ExceptionApprovedAt is null)
        {
            snapshot.ExceptionApprovedByUserId = actorId;
            snapshot.ExceptionApprovedAt = now;
        }

        snapshot.ExceptionReason = reason;
    }

    private static string? Validate(UpdateRobotsPolicy command)
    {
        if (string.IsNullOrWhiteSpace(command.Origin)) return "Select an origin.";
        if (command.ExceptionReason is { } reason && reason.Trim().Length > 500)
        {
            return "The exception reason must be 500 characters or fewer.";
        }

        // A required sitemap with no configured URL is legitimate: robots.txt may name one, and
        // the refresh follows its Sitemap directives.
        if (string.IsNullOrWhiteSpace(command.ConfiguredSitemapUrl)) return null;
        var url = command.ConfiguredSitemapUrl.Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && url.Length <= 2048
                ? null
                : "The sitemap URL must be an absolute http or https URL.";
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RobotsPolicyAuditSnapshot ToAudit(RobotsSnapshot snapshot) => new(
        snapshot.Origin,
        snapshot.SitemapRequired,
        snapshot.ConfiguredSitemapUrl is not null,
        snapshot.ExceptionReason is not null,
        snapshot.ExceptionReason,
        snapshot.Version);
}

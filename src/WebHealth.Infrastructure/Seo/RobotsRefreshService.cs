using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Seo;

public sealed record RobotsRefreshResult(int OriginsRefreshed);

/// <summary>
/// BR-E06 to BR-E08. Refreshes one snapshot per **origin**, on its own schedule, never on the path
/// that finalises a check. The fetch goes through the same <see cref="ISafeHttpTransport" /> as
/// every other outbound request, so it inherits the actual-connection SSRF control, the destination
/// policy and the bounded body rather than opening a second network surface.
/// </summary>
internal sealed class RobotsRefreshService(
    ApplicationDbContext dbContext,
    ISafeHttpTransport transport,
    SeoSchedulingOptions options,
    TimeProvider timeProvider)
{
    /// <summary>The limit search engines document for robots.txt; larger is not a robots file.</summary>
    public const int MaxRobotsBytes = 512 * 1024;

    /// <summary>A sitemap is checked for reachability only, so its body is never needed.</summary>
    private const int MaxSitemapBytes = 8 * 1024;

    private const int MaxSitemapCandidates = 3;

    public async Task<RobotsRefreshResult> RefreshDueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var refreshed = 0;
        foreach (var origin in await FindDueOriginsAsync(now, cancellationToken))
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await RobotsOriginLock.AcquireAsync(dbContext, origin.Origin, cancellationToken);
            if (!await TryClaimAsync(origin, now, cancellationToken))
            {
                // Another worker holds this origin for this TTL. One fetch per origin is the whole
                // point of the design, so losing the claim is a success, not something to retry.
                continue;
            }

            await RefreshOriginAsync(origin, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            refreshed++;
        }

        return new(refreshed);
    }

    private sealed record DueOrigin(
        string Origin, Guid EndpointId, string Host, int Port, bool IsProduction);

    /// <summary>
    /// The representative endpoint is the earliest one that carries current target authorization
    /// for this host and port. Picking an arbitrary endpoint could hand the fetch an unauthorized
    /// context and skip an origin the project is entitled to read; an origin with no authorized
    /// endpoint at all is skipped deliberately, because nothing authorises us to fetch from it.
    /// </summary>
    private async Task<IReadOnlyList<DueOrigin>> FindDueOriginsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Origin extraction is a string operation the provider cannot translate, so the candidate
        // rows are materialised first and grouped in memory.
        var candidates = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.DeletedAt == null && endpoint.IsEnabled
                && endpoint.Environment.DeletedAt == null && endpoint.Environment.IsActive
                && endpoint.TargetAuthorizations.Any(evidence =>
                    evidence.RevokedAt == null
                    && evidence.NormalizedHost == endpoint.NormalizedHost
                    && evidence.Port == endpoint.EffectivePort
                    && evidence.EffectiveFrom <= now
                    && (evidence.ExpiresAt == null || evidence.ExpiresAt > now)))
            .OrderBy(endpoint => endpoint.CreatedAt)
            .Select(endpoint => new
            {
                endpoint.Id,
                endpoint.NormalizedUrl,
                endpoint.NormalizedHost,
                endpoint.EffectivePort,
                endpoint.Environment.IsProduction
            })
            .ToArrayAsync(cancellationToken);

        var fresh = (await dbContext.RobotsSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.ExpiresAt > now)
            .Select(snapshot => snapshot.Origin)
            .ToArrayAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        return [.. candidates
            .GroupBy(row => OriginOf(row.NormalizedUrl), StringComparer.Ordinal)
            .Where(group => !fresh.Contains(group.Key))
            .Select(group => new DueOrigin(
                group.Key, group.First().Id, group.First().NormalizedHost,
                group.First().EffectivePort, group.First().IsProduction))
            .Take(options.RefreshBatchSize)];
    }

    /// <summary>
    /// Claims the origin by moving its expiry forward before any request is made. Two workers that
    /// both saw the origin as due cannot both fetch it: the claim is a conditional update, and the
    /// insert races on the primary key, so exactly one wins.
    /// </summary>
    private async Task<bool> TryClaimAsync(DueOrigin origin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expiry = now.AddHours(options.RobotsTtlHours);
        var claimed = await dbContext.RobotsSnapshots
            .Where(snapshot => snapshot.Origin == origin.Origin && snapshot.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(snapshot => snapshot.ExpiresAt, expiry),
                cancellationToken);
        if (claimed > 0) return true;

        if (await dbContext.RobotsSnapshots.AnyAsync(
            snapshot => snapshot.Origin == origin.Origin, cancellationToken))
        {
            return false;
        }

        dbContext.RobotsSnapshots.Add(new RobotsSnapshot
        {
            Origin = origin.Origin,
            Host = origin.Host,
            Port = origin.Port,
            Status = RobotsSnapshotStatuses.Unavailable,
            FetchedAt = now,
            ExpiresAt = expiry,
            UpdatedAt = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateOrigin(exception))
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task RefreshOriginAsync(
        DueOrigin origin,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // BR-E06: always the origin root, never relative to a nested endpoint path.
        var result = await transport.SendAsync(
            new(origin.EndpointId, $"{origin.Origin}/robots.txt", origin.IsProduction,
                MaxRedirects: 3, MaxResponseBodyBytes: MaxRobotsBytes,
                TimeoutSeconds: options.FetchTimeoutSeconds),
            cancellationToken);

        dbContext.ChangeTracker.Clear();
        var snapshot = await dbContext.RobotsSnapshots
            .SingleAsync(item => item.Origin == origin.Origin, cancellationToken);

        snapshot.Host = origin.Host;
        snapshot.Port = origin.Port;
        snapshot.HttpStatus = result.StatusCode;
        (snapshot.Status, snapshot.Content) = Classify(result);
        snapshot.FetchedAt = now;
        snapshot.ExpiresAt = now.AddHours(options.RobotsTtlHours);
        snapshot.UpdatedAt = now;

        await ApplySitemapAsync(snapshot, origin, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A 404 is a valid answer: no robots.txt means nothing is disallowed, which is a different
    /// fact from an origin that could not answer at all.
    /// <para>
    /// A body that hit the read cap is <c>Unavailable</c> rather than <c>Fetched</c>. Judging a
    /// policy from a document that was cut short would report a site as crawlable because the
    /// <c>Disallow</c> arrived after the cap — the failure has to be visible, not silent.
    /// </para>
    /// </summary>
    private static (string Status, string? Content) Classify(SafeHttpTransportResult result)
    {
        if (!result.Succeeded || result.BodyTruncated) return (RobotsSnapshotStatuses.Unavailable, null);
        return result.StatusCode switch
        {
            >= 200 and <= 299 => (RobotsSnapshotStatuses.Fetched, Encoding.UTF8.GetString(result.Body.Span)),
            404 or 410 => (RobotsSnapshotStatuses.NotFound, null),
            _ => (RobotsSnapshotStatuses.Unavailable, null)
        };
    }

    /// <summary>
    /// BR-E08. Candidates are the configured URL first, then the file's own Sitemap directives,
    /// which are absolute by specification. Only the status is recorded: a sitemap body is large
    /// and says nothing a status code does not.
    /// </summary>
    private async Task ApplySitemapAsync(
        RobotsSnapshot snapshot,
        DueOrigin origin,
        CancellationToken cancellationToken)
    {
        snapshot.CheckedSitemapUrl = null;
        snapshot.SitemapHttpStatus = null;
        snapshot.SitemapAvailable = false;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.ConfiguredSitemapUrl))
        {
            candidates.Add(snapshot.ConfiguredSitemapUrl);
        }

        candidates.AddRange(RobotsTxtParser.Parse(snapshot.Content).Sitemaps);
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal).Take(MaxSitemapCandidates))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var result = await transport.SendAsync(
                new(origin.EndpointId, url.AbsoluteUri, origin.IsProduction,
                    MaxRedirects: 3, MaxResponseBodyBytes: MaxSitemapBytes,
                    TimeoutSeconds: options.FetchTimeoutSeconds),
                cancellationToken);

            snapshot.CheckedSitemapUrl = Bounded(url.AbsoluteUri, 2048);
            snapshot.SitemapHttpStatus = result.StatusCode;
            snapshot.SitemapAvailable = result.Succeeded && result.StatusCode is >= 200 and <= 299;
            if (snapshot.SitemapAvailable) return;
        }
    }

    private static string Bounded(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsDuplicateOrigin(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// The origin of a normalized URL: everything before the path. Normalization has already
    /// lowercased the host and made the port explicit, so this is a string operation.
    /// </summary>
    public static string OriginOf(string normalizedUrl)
    {
        var schemeEnd = normalizedUrl.IndexOf("//", StringComparison.Ordinal) + 2;
        var pathStart = normalizedUrl.IndexOf('/', schemeEnd);
        return pathStart < 0 ? normalizedUrl : normalizedUrl[..pathStart];
    }
}

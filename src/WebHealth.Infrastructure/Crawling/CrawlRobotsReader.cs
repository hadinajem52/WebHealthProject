using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Crawling;
using WebHealth.Application.Seo;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// BR-L02. Reads the per-origin snapshot 6.4 maintains. The crawl performs no robots fetch of its
/// own: one origin means one fetch per TTL, whether fifty endpoints or one crawl depends on it.
/// <para>
/// An expired snapshot is not evidence, for the same reason the check path refuses one. A run whose
/// origin has no current snapshot crawls — absence of evidence is not a prohibition, and inventing
/// one would silently stop every crawl whose refresh job is behind.
/// </para>
/// </summary>
internal sealed class CrawlRobotsReader(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : ICrawlRobotsReader
{
    public async Task<CrawlRobotsFacts> GetAsync(string origin, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var snapshot = await dbContext.RobotsSnapshots.AsNoTracking()
            .Where(item => item.Origin == origin && item.ExpiresAt > now)
            .Select(item => new { item.Status, item.Content, item.ExceptionReason })
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot is null) return CrawlRobotsFacts.Unknown;

        // NotFound and Unavailable both mean there is no policy text to obey. They are different
        // facts for reporting, and the same fact for a crawl decision.
        var hasPolicy = snapshot.Status == RobotsSnapshotStatuses.Fetched;
        return new(hasPolicy, hasPolicy ? snapshot.Content : null, snapshot.ExceptionReason is not null);
    }
}

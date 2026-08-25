using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Crawling;
using WebHealth.Application.Seo;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

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

        var hasPolicy = snapshot.Status == RobotsSnapshotStatuses.Fetched;
        return new(hasPolicy, hasPolicy ? snapshot.Content : null, snapshot.ExceptionReason is not null);
    }
}

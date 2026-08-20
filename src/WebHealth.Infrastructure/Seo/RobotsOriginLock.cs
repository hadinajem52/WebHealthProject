using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Seo;

internal static class RobotsOriginLock
{
    public static Task AcquireAsync(
        ApplicationDbContext dbContext,
        string origin,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({origin}, 0))",
            cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal static class RetentionTransactionLock
{
    public static Task AcquireAsync(ApplicationDbContext database, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(761924, 1)", cancellationToken);
}

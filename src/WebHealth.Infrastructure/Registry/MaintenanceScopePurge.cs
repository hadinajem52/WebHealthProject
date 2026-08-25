using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Maintenance;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal static class MaintenanceScopePurge
{
    public static async Task RemoveTargetsAsync(
        ApplicationDbContext dbContext,
        Expression<Func<MaintenanceTarget, bool>> scope,
        CancellationToken cancellationToken)
    {
        var windowIds = await dbContext.MaintenanceTargets.AsNoTracking()
            .Where(scope)
            .Select(target => target.MaintenanceWindowId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (windowIds.Length == 0)
        {
            return;
        }

        await dbContext.MaintenanceTargets.Where(scope).ExecuteDeleteAsync(cancellationToken);

        var orphanedWindowIds = await dbContext.MaintenanceWindows.AsNoTracking()
            .Where(window => windowIds.Contains(window.Id)
                && !dbContext.MaintenanceTargets.Any(target => target.MaintenanceWindowId == window.Id))
            .Select(window => window.Id)
            .ToArrayAsync(cancellationToken);
        await dbContext.MaintenanceOccurrences
            .Where(occurrence => orphanedWindowIds.Contains(occurrence.MaintenanceWindowId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MaintenanceWindows
            .Where(window => orphanedWindowIds.Contains(window.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

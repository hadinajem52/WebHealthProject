using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebHealth.Application.Maintenance;
using WebHealth.Domain.Maintenance;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceOccurrenceExpander(
    ApplicationDbContext dbContext,
    MaintenanceSchedulingOptions options,
    TimeProvider timeProvider,
    ILogger<MaintenanceOccurrenceExpander> logger) : IMaintenanceOccurrenceExpander
{
    public async Task<int> ExpandWindowAsync(Guid maintenanceWindowId, CancellationToken cancellationToken = default)
    {
        var window = await dbContext.MaintenanceWindows
            .SingleOrDefaultAsync(item => item.Id == maintenanceWindowId, cancellationToken);
        if (window is null || window.DeletedAt is not null
            || !MaintenanceRecurrencePatterns.IsRecurring(window.RecurrencePattern))
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var horizon = now.AddDays(options.HorizonDays);
        var from = window.ExpandedThrough ?? window.ScheduleStartsAt;
        if (from >= horizon) return 0;

        var existingStarts = (await dbContext.MaintenanceOccurrences.AsNoTracking()
            .Where(item => item.MaintenanceWindowId == window.Id && item.StartsAt >= from && item.StartsAt < horizon)
            .Select(item => item.StartsAt)
            .ToArrayAsync(cancellationToken)).ToHashSet();

        if (!MaintenanceScheduleExpansion.TryMaterialise(
            window, from, horizon, now, existingStarts, out var occurrences))
        {
            logger.LogError(
                "Maintenance window {MaintenanceWindowId} was not expanded: timezone {TimezoneId} is unavailable.",
                window.Id, window.TimezoneId);
            return 0;
        }

        if (occurrences.Count > 0) dbContext.MaintenanceOccurrences.AddRange(occurrences);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.MaintenanceWindows
                .Where(item => item.Id == window.Id
                    && (item.ExpandedThrough == null || item.ExpandedThrough < horizon))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpandedThrough, horizon),
                    cancellationToken);
            return occurrences.Count;
        }
        catch (DbUpdateException exception) when (IsDuplicateOccurrence(exception))
        {
            dbContext.ChangeTracker.Clear();
            return 0;
        }
    }

    public async Task<MaintenanceExpansionResult> ExpandDueAsync(CancellationToken cancellationToken = default)
    {
        var horizon = timeProvider.GetUtcNow().AddDays(options.HorizonDays);
        var due = await dbContext.MaintenanceWindows.AsNoTracking()
            .Where(window => window.DeletedAt == null
                && window.RecurrencePattern != MaintenanceRecurrencePatterns.None
                && (window.ExpandedThrough == null || window.ExpandedThrough < horizon)
                && (window.RecurrenceUntil == null || window.ExpandedThrough == null
                    || window.RecurrenceUntil > window.ExpandedThrough))
            .OrderBy(window => window.ExpandedThrough)
            .Select(window => window.Id)
            .Take(options.BatchSize)
            .ToArrayAsync(cancellationToken);

        var created = 0;
        foreach (var windowId in due)
        {
            created += await ExpandWindowAsync(windowId, cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        return new(due.Length, created);
    }

    private static bool IsDuplicateOccurrence(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_maintenance_occurrence_window_start"
        };
}

using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class IncidentRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<IncidentRetentionBatch> logger)
{
    public async Task<RetentionBatchResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (!options.Enabled) return new(0, 0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.MaximumRunDuration);
        var token = deadline.Token;
        var stopwatch = Stopwatch.StartNew();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await RetentionTransactionLock.AcquireAsync(database, token);
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddMonths(-24);
        var held = new RetentionHoldQueries(database, now).IncidentIds();
        var candidates = database.Incidents.Where(incident =>
            ((incident.Status == "Resolved" && incident.ResolvedAt < cutoff)
                || (incident.Status == "Closed" && incident.ClosedAt < cutoff))
            && !held.Contains(incident.Id)
            && !database.Incidents.Any(successor => successor.PreviousIncidentId == incident.Id && held.Contains(successor.Id))
            && !database.NotificationDeliveries.Any(delivery => delivery.NotificationEvent.IncidentId == incident.Id
                && (delivery.State == "Pending" || delivery.State == "Processing" || delivery.State == "RetryScheduled"
                    || delivery.LeaseOwner != null || delivery.LeaseExpiresAt != null)));
        var ids = await candidates.OrderBy(incident => incident.ClosedAt ?? incident.ResolvedAt).ThenBy(incident => incident.Id)
            .Select(incident => incident.Id).Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Incidents.FromSqlInterpolated($"""
                SELECT * FROM web_health.incident WHERE id = ANY({ids}) ORDER BY id FOR UPDATE
                """).AsNoTracking().ToArrayAsync(token);
            await database.NotificationDeliveries.FromSqlInterpolated($"""
                SELECT delivery.* FROM web_health.notification_delivery delivery
                JOIN web_health.notification_event notification ON notification.id = delivery.notification_event_id
                WHERE notification.incident_id = ANY({ids}) ORDER BY delivery.id FOR UPDATE OF delivery
                """).AsNoTracking().ToArrayAsync(token);
            ids = await candidates.Where(incident => ids.Contains(incident.Id)).Select(incident => incident.Id).ToArrayAsync(token);
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            await DetachSuccessorsAsync(ids, now, token);
            var notifications = database.NotificationEvents.Where(item => ids.Contains(item.IncidentId)).Select(item => item.Id);
            var deliveries = database.NotificationDeliveries.Where(item => notifications.Contains(item.NotificationEventId)).Select(item => item.Id);
            await database.NotificationAttempts.Where(item => deliveries.Contains(item.NotificationDeliveryId)).ExecuteDeleteAsync(token);
            await database.NotificationDeliveries.Where(item => notifications.Contains(item.NotificationEventId)).ExecuteDeleteAsync(token);
            await database.NotificationEvents.Where(item => ids.Contains(item.IncidentId)).ExecuteDeleteAsync(token);
            await database.IncidentEvidence.Where(item => ids.Contains(item.IncidentId)).ExecuteDeleteAsync(token);
            await database.IncidentEvents.Where(item => ids.Contains(item.IncidentId)).ExecuteDeleteAsync(token);
            await database.Incidents.Where(item => ids.Contains(item.Id) && item.PreviousIncidentId != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PreviousIncidentId, (Guid?)null), token);
            deleted = await database.Incidents.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention incident-bundle selected {SelectedCount} and deleted {DeletedCount} roots in {DurationMs} ms; dry run {DryRun}",
            ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }

    private async Task DetachSuccessorsAsync(Guid[] ids, DateTimeOffset now, CancellationToken token)
    {
        var successors = await database.Incidents.FromSqlInterpolated($"""
            SELECT * FROM web_health.incident
            WHERE previous_incident_id = ANY({ids}) AND NOT (id = ANY({ids})) ORDER BY id FOR UPDATE
            """).AsNoTracking().ToArrayAsync(token);
        foreach (var successor in successors)
        {
            await database.Incidents.Where(item => item.Id == successor.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PreviousIncidentId, (Guid?)null)
                    .SetProperty(item => item.Version, item => item.Version + 1), token);
            database.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                ActorIdentifier = "system:monitoring-retention",
                OccurredAt = now,
                Action = "incident.retention_lineage_truncated",
                EntityType = "incident",
                EntityIdentifier = successor.Id.ToString(),
                Outcome = "succeeded",
                BeforeValues = JsonSerializer.Serialize(new
                {
                    successor.PreviousIncidentId,
                    successor.RecurrenceCount
                }),
                AfterValues = JsonSerializer.Serialize(new
                {
                    PreviousIncidentId = (Guid?)null,
                    successor.RecurrenceCount
                })
            });
        }
        await database.SaveChangesAsync(token);
    }
}

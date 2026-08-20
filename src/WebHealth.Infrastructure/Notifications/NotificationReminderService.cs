using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Application.Maintenance;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Notifications;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Notifications;

public sealed record NotificationReminderSweepResult(int RemindersWritten, int EscalationsWritten);

/// <summary>
/// Sweeps unacknowledged critical incidents for the 60-minute reminder and the single 30-minute
/// escalation level. Acknowledgement stops both automatically — an incident with
/// AcknowledgedAt set no longer matches the candidate query, so nothing further is written for
/// it. Each incident is written in its own transaction. Once a slot's notification has been
/// sent, every later tick for that same incident matches the same deterministic occurrence key
/// until the next boundary, so an existence check short-circuits the common case before it ever
/// reaches the database's unique index; the unique-key catch stays only as the backstop for an
/// actual race (a second sweep tick landing between the check and the insert).
/// </summary>
internal sealed class NotificationReminderService(
    ApplicationDbContext dbContext,
    IMaintenanceEvaluator maintenanceEvaluator,
    NotificationEventWriter notificationEventWriter,
    NotificationSchedulingOptions options,
    TimeProvider timeProvider)
{
    public async Task<NotificationReminderSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var candidates = await dbContext.Incidents.AsNoTracking()
            .Where(incident => incident.Severity == IncidentSeverities.Critical
                && IncidentStatuses.Active.Contains(incident.Status)
                && incident.AcknowledgedAt == null)
            .Select(incident => new { incident.Id, incident.EndpointMonitorId, incident.OpenedAt })
            .ToArrayAsync(cancellationToken);

        var reminders = 0;
        var escalations = 0;
        foreach (var candidate in candidates)
        {
            var maintenance = await maintenanceEvaluator.FindActiveAsync(
                candidate.EndpointMonitorId, now, cancellationToken);
            if (maintenance is not null)
            {
                // BR-M03: escalation pauses during an active maintenance window by default; skip
                // this tick rather than accumulating a reminder/escalation for paused time.
                continue;
            }

            var unacknowledged = now - candidate.OpenedAt;
            if (unacknowledged >= options.EscalationDelay
                && await TryWriteAsync(
                    candidate.Id,
                    NotificationSourceKinds.Escalation,
                    NotificationEventTypes.Escalated,
                    NotificationOccurrenceKeys.Escalation(candidate.Id),
                    now,
                    cancellationToken))
            {
                escalations++;
            }

            var reminderSlot = (int)(unacknowledged / options.ReminderInterval);
            if (reminderSlot >= 1
                && await TryWriteAsync(
                    candidate.Id,
                    NotificationSourceKinds.Reminder,
                    NotificationEventTypes.Reminder,
                    NotificationOccurrenceKeys.Reminder(candidate.Id, reminderSlot),
                    now,
                    cancellationToken))
            {
                reminders++;
            }
        }

        return new(reminders, escalations);
    }

    private async Task<bool> TryWriteAsync(
        Guid incidentId,
        string sourceKind,
        string eventType,
        string occurrenceKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alreadySent = await dbContext.NotificationEvents.AsNoTracking().AnyAsync(
            candidate => candidate.IncidentId == incidentId
                && candidate.SourceKind == sourceKind
                && candidate.EventType == eventType
                && candidate.OccurrenceKey == occurrenceKey,
            cancellationToken);
        if (alreadySent)
        {
            return false;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var incident = await dbContext.Incidents.SingleAsync(
                candidate => candidate.Id == incidentId, cancellationToken);
            await notificationEventWriter.WriteAsync(
                incident, null, sourceKind, eventType, occurrenceKey, isMaintenance: false, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}

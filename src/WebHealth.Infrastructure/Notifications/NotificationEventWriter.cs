using Microsoft.EntityFrameworkCore;
using WebHealth.Domain.Normalization;
using WebHealth.Domain.Notifications;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Notifications;

internal sealed class NotificationEventWriter(ApplicationDbContext dbContext)
{
    public async Task<NotificationEvent> WriteAsync(
        Incident incident,
        Guid? incidentEventId,
        string sourceKind,
        string eventType,
        string occurrenceKey,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        TimeSpan? notifyDelay = null)
    {
        var notificationEvent = new NotificationEvent
        {
            Id = Guid.NewGuid(),
            IncidentEventId = incidentEventId,
            IncidentId = incident.Id,
            SourceKind = sourceKind,
            EventType = eventType,
            OccurrenceKey = occurrenceKey,
            TemplateVersion = Application.Notifications.NotificationTemplates.Version,
            IsSuppressed = isMaintenance,
            SuppressionReason = isMaintenance ? "ActiveMaintenanceWindow" : null,
            OccurredAt = now
        };
        dbContext.NotificationEvents.Add(notificationEvent);

        var recipients = await ResolveRecipientsAsync(incident.OwnerSubjectId, now, cancellationToken);
        foreach (var recipient in recipients)
        {
            dbContext.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(),
                NotificationEventId = notificationEvent.Id,
                Channel = NotificationChannels.Email,
                NormalizedRecipient = recipient,
                RecipientNormalizationVersion = RecipientNormalizer.Version,
                State = isMaintenance ? NotificationDeliveryStates.Suppressed : NotificationDeliveryStates.Pending,
                AttemptCount = 0,
                NextAttemptAt = isMaintenance ? null : now + (notifyDelay ?? TimeSpan.Zero)
            });
        }

        return notificationEvent;
    }

    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        Guid ownerSubjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ownerSubject = await dbContext.OwnerSubjects.AsNoTracking()
            .SingleAsync(subject => subject.Id == ownerSubjectId, cancellationToken);

        List<string?> rawAddresses;
        if (ownerSubject.UserId is { } userId)
        {
            rawAddresses = await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == userId && !user.IsDisabled)
                .Select(user => user.Email)
                .ToListAsync(cancellationToken);
        }
        else
        {
            rawAddresses = await dbContext.TeamMembers.AsNoTracking()
                .Where(member => member.TeamId == ownerSubject.TeamId
                    && member.EffectiveFrom <= now
                    && (member.EffectiveUntil == null || member.EffectiveUntil > now)
                    && !member.User.IsDisabled)
                .Select(member => member.User.Email)
                .ToListAsync(cancellationToken);
        }

        return rawAddresses
            .Select(RecipientNormalizer.Normalize)
            .Where(email => email is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(email => email!)
            .ToArray();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Notifications;
using WebHealth.Domain.Notifications;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Notifications;

public sealed record NotificationDispatchResult(int Claimed, int Sent);

/// <summary>
/// Claims due (Pending/RetryScheduled with an elapsed next_attempt_at) and stale-leased
/// (Processing past its lease) deliveries in one query, so a single recurring tick is both the
/// normal dispatcher and the restart/crash reconciliation pass — there is nothing left mid-flight
/// for a separate reconciler to find. Each claimed delivery is sent and updated in its own short
/// transaction, entirely outside the finalization transaction that created it.
/// </summary>
internal sealed class NotificationDispatchService(
    ApplicationDbContext dbContext,
    IEmailTransport emailTransport,
    NotificationSchedulingOptions options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatchService> logger)
{
    public async Task<NotificationDispatchResult> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var leaseOwner = Guid.NewGuid().ToString("N");
        var leaseExpiresAt = now.Add(options.LeaseDuration);
        var claimedIds = await ClaimDueDeliveriesAsync(now, leaseOwner, leaseExpiresAt, cancellationToken);

        var sent = 0;
        foreach (var deliveryId in claimedIds)
        {
            if (await SendAsync(deliveryId, leaseOwner, now, cancellationToken))
            {
                sent++;
            }
        }

        return new(claimedIds.Count, sent);
    }

    private async Task<IReadOnlyList<Guid>> ClaimDueDeliveriesAsync(
        DateTimeOffset now,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var ids = new List<Guid>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT id FROM web_health.notification_delivery
            WHERE (state IN ('Pending', 'RetryScheduled') AND next_attempt_at <= @now)
               OR (state = 'Processing' AND lease_expires_at <= @now)
            ORDER BY next_attempt_at NULLS FIRST, id
            FOR UPDATE SKIP LOCKED
            LIMIT @limit
            """,
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            select.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, options.DispatchBatchSize);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        if (ids.Count > 0)
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE web_health.notification_delivery
                SET state = 'Processing', lease_owner = @lease_owner, lease_expires_at = @lease_expires_at
                WHERE id = ANY(@ids)
                """,
                connection,
                (NpgsqlTransaction)transaction.GetDbTransaction());
            update.Parameters.AddWithValue("lease_owner", NpgsqlDbType.Text, leaseOwner);
            update.Parameters.AddWithValue("lease_expires_at", NpgsqlDbType.TimestampTz, leaseExpiresAt);
            update.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, ids.ToArray());
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }

    private async Task<bool> SendAsync(
        Guid deliveryId,
        string leaseOwner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var delivery = await dbContext.NotificationDeliveries
            .Include(candidate => candidate.NotificationEvent).ThenInclude(notificationEvent => notificationEvent.Incident)
                .ThenInclude(incident => incident.EndpointMonitor).ThenInclude(monitor => monitor.Endpoint)
                    .ThenInclude(endpoint => endpoint.Environment).ThenInclude(environment => environment.Website)
                        .ThenInclude(website => website.Client)
            .Include(candidate => candidate.NotificationEvent).ThenInclude(notificationEvent => notificationEvent.Incident)
                .ThenInclude(incident => incident.OwnerSubject)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == deliveryId && candidate.LeaseOwner == leaseOwner,
                cancellationToken);
        if (delivery is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var ownerDisplayName = await ResolveOwnerDisplayNameAsync(delivery.NotificationEvent.Incident.OwnerSubject, cancellationToken);
        var message = BuildMessage(delivery, ownerDisplayName);
        var result = await emailTransport.SendAsync(message, cancellationToken);
        delivery.AttemptCount++;
        dbContext.NotificationAttempts.Add(new NotificationAttempt
        {
            Id = Guid.NewGuid(),
            NotificationDeliveryId = delivery.Id,
            AttemptNumber = delivery.AttemptCount,
            TransportOutcome = ToOutcome(result.Outcome),
            SafeResponse = result.SafeResponse,
            AttemptedAt = now
        });

        ApplyOutcome(delivery, result.Outcome, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (result.Outcome != EmailTransportOutcome.Sent)
        {
            logger.LogWarning(
                "Notification delivery {DeliveryId} outcome {Outcome} on attempt {AttemptCount}.",
                delivery.Id, result.Outcome, delivery.AttemptCount);
        }

        return result.Outcome == EmailTransportOutcome.Sent;
    }

    private void ApplyOutcome(NotificationDelivery delivery, EmailTransportOutcome outcome, DateTimeOffset now)
    {
        delivery.LeaseOwner = null;
        delivery.LeaseExpiresAt = null;
        if (outcome == EmailTransportOutcome.Sent)
        {
            delivery.State = NotificationDeliveryStates.Sent;
            delivery.SentAt = now;
            delivery.NextAttemptAt = null;
            return;
        }

        if (outcome == EmailTransportOutcome.TransientFailure && delivery.AttemptCount < options.MaxAttempts)
        {
            delivery.State = NotificationDeliveryStates.RetryScheduled;
            delivery.NextAttemptAt = now.Add(BackoffFor(delivery.AttemptCount));
            return;
        }

        delivery.State = NotificationDeliveryStates.FailedPermanently;
        delivery.NextAttemptAt = null;
    }

    private TimeSpan BackoffFor(int attemptCount)
    {
        var scaled = options.InitialRetryDelay * attemptCount;
        return scaled > options.MaxRetryDelay ? options.MaxRetryDelay : scaled;
    }

    private static string ToOutcome(EmailTransportOutcome outcome) => outcome switch
    {
        EmailTransportOutcome.Sent => NotificationTransportOutcomes.Sent,
        EmailTransportOutcome.TransientFailure => NotificationTransportOutcomes.TransientFailure,
        _ => NotificationTransportOutcomes.PermanentFailure
    };

    private static EmailMessage BuildMessage(NotificationDelivery delivery, string ownerDisplayName)
    {
        var notificationEvent = delivery.NotificationEvent;
        var incident = notificationEvent.Incident;
        var endpoint = incident.EndpointMonitor.Endpoint;
        var website = endpoint.Environment.Website;
        var data = new NotificationTemplateData(
            incident.Id,
            endpoint.DisplayUrl,
            website.Client.Name,
            website.Name,
            endpoint.Environment.Name,
            incident.IssueKey,
            incident.Severity,
            incident.OpenedAt,
            ownerDisplayName,
            $"/incidents/{incident.Id}",
            incident.ResolvedAt,
            incident.OutageDurationMs);
        var (subject, body) = notificationEvent.EventType switch
        {
            NotificationEventTypes.Opened => NotificationTemplates.RenderOpened(data),
            NotificationEventTypes.Recovered => NotificationTemplates.RenderRecovered(data),
            NotificationEventTypes.Reminder => NotificationTemplates.RenderReminder(data),
            NotificationEventTypes.Escalated => NotificationTemplates.RenderEscalated(data),
            _ => throw new InvalidOperationException($"Unsupported notification event type '{notificationEvent.EventType}'.")
        };
        return new EmailMessage(delivery.NormalizedRecipient, subject, body);
    }

    private async Task<string> ResolveOwnerDisplayNameAsync(
        Assignments.OwnerSubject ownerSubject,
        CancellationToken cancellationToken)
    {
        if (ownerSubject.UserId is { } userId)
        {
            var name = await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.DisplayName)
                .SingleOrDefaultAsync(cancellationToken);
            return name ?? "Assigned owner";
        }

        var teamName = await dbContext.Teams.AsNoTracking()
            .Where(team => team.Id == ownerSubject.TeamId)
            .Select(team => team.Name)
            .SingleOrDefaultAsync(cancellationToken);
        return teamName ?? "Assigned team";
    }
}

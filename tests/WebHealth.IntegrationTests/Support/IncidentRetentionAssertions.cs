using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Notifications;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Notifications;

namespace WebHealth.IntegrationTests.Support;

internal static class IncidentRetentionAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(builder, connectionString);
        await using var database = new ApplicationDbContext(builder.Options);
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint.Environment.Website).SingleAsync(item => item.Id == monitorId);
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var cutoff = now.AddMonths(-24);
        var incidents = new Dictionary<string, Incident>();
        foreach (var name in new[] { "eligible-closed", "eligible-resolved", "held", "active", "boundary", "pending", "leased", "predecessor", "successor", "held-successor" })
        {
            var active = name is "active" or "successor" or "held-successor";
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                EndpointMonitorId = monitorId,
                OwnerSubjectId = monitor.Endpoint.OwnerSubjectId ?? monitor.Endpoint.Environment.Website.OwnerSubjectId,
                IssueKey = "ControlledRetention." + name,
                Severity = "Critical",
                Status = active ? "Open" : name == "eligible-resolved" ? "Resolved" : "Closed",
                OpenedAt = cutoff.AddDays(-1),
                ResolvedAt = active ? null : cutoff.AddTicks(name == "eligible-closed" ? -20 : -10),
                ClosedAt = active || name == "eligible-resolved" ? null : name == "boundary" ? cutoff : cutoff.AddTicks(name == "eligible-closed" ? -20 : -10),
                ResolutionCategory = active ? null : "Recovered",
                ResolutionNote = active ? null : "Controlled retention fixture",
                Version = 2,
                RecurrenceCount = name == "successor" ? 4 : 0
            };
            database.Incidents.Add(incident);
            incidents.Add(name, incident);
        }
        incidents["successor"].PreviousIncidentId = incidents["eligible-closed"].Id;
        incidents["held-successor"].PreviousIncidentId = incidents["predecessor"].Id;
        var events = new Dictionary<Guid, Guid>();
        var notifications = new Dictionary<Guid, Guid>();
        var deliveries = new Dictionary<Guid, Guid>();
        foreach (var pair in incidents)
        {
            var incident = pair.Value;
            var incidentEventId = Guid.NewGuid();
            events.Add(incident.Id, incidentEventId);
            database.IncidentEvents.Add(new IncidentEvent
            {
                Id = incidentEventId,
                IncidentId = incident.Id,
                SequenceNumber = 1,
                EventType = "Opened",
                ToStatus = "Open",
                OccurredAt = incident.OpenedAt
            });
            database.IncidentEvidence.Add(new IncidentEvidence
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                EndpointMonitorId = monitorId,
                ActorUserId = monitor.Endpoint.CreatedByUserId,
                EvidenceType = "Resolution",
                EvidenceRole = "ControlledRetention",
                BoundedSnapshot = "{}",
                CapturedAt = cutoff.AddTicks(-10)
            });
            var notificationId = Guid.NewGuid();
            notifications.Add(incident.Id, notificationId);
            database.NotificationEvents.Add(new NotificationEvent
            {
                Id = notificationId,
                IncidentId = incident.Id,
                IncidentEventId = incidentEventId,
                SourceKind = "IncidentEvent",
                EventType = "Opened",
                OccurrenceKey = incidentEventId.ToString(),
                TemplateVersion = "v1",
                OccurredAt = incident.OpenedAt
            });
            var deliveryId = Guid.NewGuid();
            deliveries.Add(incident.Id, deliveryId);
            database.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = deliveryId,
                NotificationEventId = notificationId,
                Channel = "Email",
                NormalizedRecipient = "retention@example.test",
                RecipientNormalizationVersion = 1,
                State = pair.Key == "pending" ? "Pending" : "Sent",
                SentAt = pair.Key == "pending" ? null : incident.OpenedAt,
                AttemptCount = 1,
                LeaseOwner = pair.Key == "leased" ? "controlled-retention" : null,
                LeaseExpiresAt = pair.Key == "leased" ? now.AddMinutes(1) : null
            });
            database.NotificationAttempts.Add(new NotificationAttempt
            {
                Id = Guid.NewGuid(),
                NotificationDeliveryId = deliveryId,
                AttemptNumber = 1,
                TransportOutcome = pair.Key == "pending" ? "TransientFailure" : "Sent",
                AttemptedAt = incident.OpenedAt
            });
        }
        foreach (var name in new[] { "held", "held-successor" })
            database.RetentionHolds.Add(new RetentionHold
            {
                Id = Guid.NewGuid(),
                ScopeType = "Incident",
                ScopeId = incidents[name].Id,
                Reason = "Controlled incident retention",
                CreatedByUserId = monitor.Endpoint.CreatedByUserId,
                CreatedAt = now
            });
        await database.SaveChangesAsync();
        await VerifyConcurrentReopenAsync(builder.Options, incidents["eligible-closed"], now);
        IncidentRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, new RetentionClock(now), NullLogger<IncidentRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.Incidents.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(10);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION web_health.retention_fixture_reject_incident_delete() RETURNS trigger
            LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Controlled retention rollback'; END; $$;
            CREATE TRIGGER retention_fixture_reject_incident_delete BEFORE DELETE ON web_health.incident
            FOR EACH ROW EXECUTE FUNCTION web_health.retention_fixture_reject_incident_delete();
            """);
        try
        {
            var fail = async () => await Batch(true, false).ExecuteAsync();
            (await fail.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
            database.ChangeTracker.Clear();
            (await database.Incidents.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(10);
            (await database.IncidentEvents.CountAsync(item => item.Incident.EndpointMonitorId == monitorId)).Should().Be(10);
            (await database.IncidentEvidence.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(10);
            (await database.NotificationAttempts.CountAsync(item => item.Delivery.NotificationEvent.Incident.EndpointMonitorId == monitorId)).Should().Be(10);
            (await database.Incidents.AsNoTracking().SingleAsync(item => item.Id == incidents["successor"].Id)).PreviousIncidentId
                .Should().Be(incidents["eligible-closed"].Id);
            (await database.AuditEvents.CountAsync(item => item.Action == "incident.retention_lineage_truncated"
                && item.EntityIdentifier == incidents["successor"].Id.ToString())).Should().Be(0);
        }
        finally
        {
            await database.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER retention_fixture_reject_incident_delete ON web_health.incident;
                DROP FUNCTION web_health.retention_fixture_reject_incident_delete();
                """);
        }
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var preserved = incidents.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal)).Select(pair => pair.Value.Id).ToArray();
        var allIds = incidents.Values.Select(item => item.Id).ToArray();
        (await database.Incidents.Where(item => allIds.Contains(item.Id)).Select(item => item.Id).ToArrayAsync()).Should().BeEquivalentTo(preserved);
        (await database.IncidentEvents.Where(item => allIds.Contains(item.IncidentId)).Select(item => item.IncidentId).ToArrayAsync()).Should().BeEquivalentTo(preserved);
        (await database.IncidentEvidence.Where(item => allIds.Contains(item.IncidentId)).Select(item => item.IncidentId).ToArrayAsync()).Should().BeEquivalentTo(preserved);
        (await database.NotificationEvents.Where(item => allIds.Contains(item.IncidentId)).Select(item => item.IncidentId).ToArrayAsync()).Should().BeEquivalentTo(preserved);
        var allNotifications = notifications.Values.ToArray();
        (await database.NotificationDeliveries.Where(item => allNotifications.Contains(item.NotificationEventId)).Select(item => item.Id).ToArrayAsync())
            .Should().BeEquivalentTo(preserved.Select(id => deliveries[id]));
        var allDeliveries = deliveries.Values.ToArray();
        (await database.NotificationAttempts.Where(item => allDeliveries.Contains(item.NotificationDeliveryId)).Select(item => item.NotificationDeliveryId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved.Select(id => deliveries[id]));
        var successor = await database.Incidents.AsNoTracking().SingleAsync(item => item.Id == incidents["successor"].Id);
        successor.PreviousIncidentId.Should().BeNull();
        successor.RecurrenceCount.Should().Be(4);
        successor.Version.Should().Be(3);
        var heldSuccessor = await database.Incidents.AsNoTracking().SingleAsync(item => item.Id == incidents["held-successor"].Id);
        heldSuccessor.PreviousIncidentId.Should().Be(incidents["predecessor"].Id);
        heldSuccessor.Version.Should().Be(2);
        var audit = await database.AuditEvents.SingleAsync(item => item.Action == "incident.retention_lineage_truncated"
            && item.EntityIdentifier == successor.Id.ToString());
        audit.ActorIdentifier.Should().Be("system:monitoring-retention");
        using var before = JsonDocument.Parse(audit.BeforeValues!);
        using var after = JsonDocument.Parse(audit.AfterValues!);
        before.RootElement.GetProperty("PreviousIncidentId").GetGuid().Should().Be(incidents["eligible-closed"].Id);
        after.RootElement.GetProperty("PreviousIncidentId").ValueKind.Should().Be(JsonValueKind.Null);
        after.RootElement.GetProperty("RecurrenceCount").GetInt32().Should().Be(4);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var execute = async () => await Batch(true, false).ExecuteAsync(cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
        await VerifyConcurrentDeliveryAsync(builder.Options, incidents["pending"].Id, deliveries[incidents["pending"].Id], now);
    }

    private static async Task VerifyConcurrentDeliveryAsync(DbContextOptions<ApplicationDbContext> options, Guid incidentId, Guid deliveryId, DateTimeOffset now)
    {
        await using var dispatchDatabase = new ApplicationDbContext(options);
        await using var retentionDatabase = new ApplicationDbContext(options);
        await dispatchDatabase.NotificationDeliveries.Where(item => item.Id == deliveryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAt, now.AddMonths(-24).AddDays(-2)));
        var transport = new PausedEmailTransport();
        var dispatch = new NotificationDispatchService(dispatchDatabase, transport, new() { DispatchBatchSize = 1 },
            new RetentionClock(now), NullLogger<NotificationDispatchService>.Instance);
        var batch = new IncidentRetentionBatch(retentionDatabase, new() { Enabled = true, DryRun = false, BatchSize = 1 },
            new RetentionClock(now), NullLogger<IncidentRetentionBatch>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sending = dispatch.DispatchDueAsync(timeout.Token);
        try
        {
            await transport.Started.Task.WaitAsync(timeout.Token);
            var delivery = await retentionDatabase.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == deliveryId);
            delivery.State.Should().Be("Processing");
            delivery.LeaseOwner.Should().NotBeNullOrEmpty();
            delivery.LeaseExpiresAt.Should().BeAfter(now);
            (await batch.ExecuteAsync(timeout.Token)).Should().Be(new RetentionBatchResult(0, 0));
            (await retentionDatabase.IncidentEvidence.CountAsync(item => item.IncidentId == incidentId)).Should().Be(1);
            (await retentionDatabase.IncidentEvents.CountAsync(item => item.IncidentId == incidentId)).Should().Be(1);
            transport.Outcome.SetResult(new(EmailTransportOutcome.TransientFailure, "Controlled retry"));
            (await sending).Should().Be(new NotificationDispatchResult(1, 0));
            delivery = await retentionDatabase.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == deliveryId);
            delivery.State.Should().Be("RetryScheduled");
            delivery.LeaseOwner.Should().BeNull();
            delivery.NextAttemptAt.Should().BeAfter(now);
            (await batch.ExecuteAsync(timeout.Token)).Should().Be(new RetentionBatchResult(0, 0));
            (await retentionDatabase.NotificationAttempts.CountAsync(item => item.NotificationDeliveryId == deliveryId)).Should().Be(2);
            (await retentionDatabase.Incidents.AnyAsync(item => item.Id == incidentId)).Should().BeTrue();
        }
        finally
        {
            await timeout.CancelAsync();
            try { await sending; }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class PausedEmailTransport : IEmailTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EmailTransportResult> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EmailTransportResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Outcome.Task.WaitAsync(cancellationToken);
        }
    }

    private static async Task VerifyConcurrentReopenAsync(DbContextOptions<ApplicationDbContext> options, Incident incident, DateTimeOffset now)
    {
        await using var mutation = new ApplicationDbContext(options);
        await using var retention = new ApplicationDbContext(options);
        await retention.Database.OpenConnectionAsync();
        var retentionPid = ((NpgsqlConnection)retention.Database.GetDbConnection()).ProcessID;
        await using var transaction = await mutation.Database.BeginTransactionAsync();
        await mutation.Incidents.Where(item => item.Id == incident.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, "Open")
            .SetProperty(item => item.ResolvedAt, (DateTimeOffset?)null)
            .SetProperty(item => item.ClosedAt, (DateTimeOffset?)null)
            .SetProperty(item => item.ResolutionCategory, (string?)null)
            .SetProperty(item => item.ResolutionNote, (string?)null));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var batch = new IncidentRetentionBatch(retention, new() { Enabled = true, DryRun = false, BatchSize = 1 },
            new RetentionClock(now), NullLogger<IncidentRetentionBatch>.Instance);
        var cleanup = batch.ExecuteAsync(timeout.Token);
        try
        {
            while (!await mutation.Database.SqlQuery<int>($"""
                SELECT CASE WHEN pg_backend_pid() = ANY(pg_blocking_pids({retentionPid}))
                    THEN 1 ELSE 0 END AS "Value"
                """).AnyAsync(count => count == 1, timeout.Token))
            {
                cleanup.IsCompleted.Should().BeFalse("retention must wait for the concurrent incident mutation");
                await Task.Delay(25, timeout.Token);
            }
            await transaction.CommitAsync(timeout.Token);
            (await cleanup).Should().Be(new RetentionBatchResult(0, 0),
                "eligibility must be checked again after the incident row lock is acquired");
            (await retention.Incidents.AsNoTracking().SingleAsync(item => item.Id == incident.Id)).Status.Should().Be("Open");
            (await retention.IncidentEvidence.CountAsync(item => item.IncidentId == incident.Id)).Should().Be(1);
            (await retention.IncidentEvents.CountAsync(item => item.IncidentId == incident.Id)).Should().Be(1);
            (await retention.NotificationAttempts.CountAsync(item => item.Delivery.NotificationEvent.IncidentId == incident.Id)).Should().Be(1);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await cleanup; }
            catch (OperationCanceledException) { }
        }
        await mutation.Incidents.Where(item => item.Id == incident.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, incident.Status)
            .SetProperty(item => item.ResolvedAt, incident.ResolvedAt)
            .SetProperty(item => item.ClosedAt, incident.ClosedAt)
            .SetProperty(item => item.ResolutionCategory, incident.ResolutionCategory)
            .SetProperty(item => item.ResolutionNote, incident.ResolutionNote));
    }

    private sealed class RetentionClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

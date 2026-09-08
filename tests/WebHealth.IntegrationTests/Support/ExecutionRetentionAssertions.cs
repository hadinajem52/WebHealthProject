using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class ExecutionRetentionAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(builder, connectionString);
        await using var database = new ApplicationDbContext(builder.Options);
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint.Environment.Website).SingleAsync(item => item.Id == monitorId);
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var cutoff = now.AddDays(-90);
        var attempts = new Dictionary<string, ExecutionAttempt>();
        foreach (var name in new[] { "eligible-a", "eligible-b", "boundary", "held", "current", "leased", "incident", "unfinished" })
        {
            var finished = name == "boundary" ? cutoff : cutoff.AddTicks(-10);
            var check = new LogicalCheck
            {
                Id = Guid.NewGuid(),
                EndpointMonitorId = monitorId,
                Source = "Manual",
                RequestedAt = finished.AddMinutes(-1),
                InitiatedByUserId = monitor.Endpoint.CreatedByUserId,
                State = name == "unfinished" ? "Running" : "Completed",
                PolicyFingerprint = monitor.ConfigurationFingerprint,
                CreatedAt = finished.AddMinutes(-1),
                QueuedAt = finished.AddMinutes(-1),
                StartedAt = finished.AddMinutes(-1),
                CompletedAt = name == "unfinished" ? null : finished
            };
            database.LogicalChecks.Add(check);
            database.CheckConfigurationSnapshots.Add(CheckConfigurationSnapshotFactory.Create(monitor, check.Id,
                check.CreatedAt, NullLogger.Instance));
            var attempt = new ExecutionAttempt
            {
                Id = Guid.NewGuid(),
                LogicalCheckId = check.Id,
                AttemptNumber = 1,
                JobId = name,
                WorkerId = "retention-fixture",
                StartedAt = check.CreatedAt,
                FinishedAt = finished,
                InfrastructureOutcome = "Succeeded"
            };
            database.ExecutionAttempts.Add(attempt);
            attempts.Add(name, attempt);
        }
        await database.SaveChangesAsync();
        database.RetentionHolds.Add(new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = "LogicalCheck",
            ScopeId = attempts["held"].LogicalCheckId,
            Reason = "Controlled retention protection",
            CreatedByUserId = monitor.Endpoint.CreatedByUserId,
            CreatedAt = now
        });
        database.EndpointHealth.Add(new EndpointHealth
        {
            EndpointMonitorId = monitorId,
            EvidenceLogicalCheckId = attempts["current"].LogicalCheckId,
            ConfirmedStatus = "Healthy",
            ConfirmedAt = now,
            Version = 1
        });
        database.ExecutionLeases.Add(new ExecutionLease
        {
            EndpointMonitorId = monitorId,
            LogicalCheckId = attempts["leased"].LogicalCheckId,
            OwnerToken = Guid.NewGuid(),
            FencingGeneration = 1,
            AcquiredAt = now,
            ExpiresAt = now.AddMinutes(1)
        });
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitorId,
            OwnerSubjectId = monitor.Endpoint.OwnerSubjectId ?? monitor.Endpoint.Environment.Website.OwnerSubjectId,
            IssueKey = "Http.ServerError",
            Severity = "Critical",
            Status = "Open",
            OpenedAt = now,
            Version = 1
        };
        database.Incidents.Add(incident);
        database.IncidentEvidence.Add(new IncidentEvidence
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            EndpointMonitorId = monitorId,
            LogicalCheckId = attempts["incident"].LogicalCheckId,
            EvidenceType = "Opening",
            EvidenceRole = "ControlledRetention",
            BoundedSnapshot = "{}",
            CapturedAt = now
        });
        await database.SaveChangesAsync();
        var clock = new RetentionClock(now);
        ExecutionHistoryRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, clock, NullLogger<ExecutionHistoryRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.ExecutionAttempts.CountAsync(item => item.LogicalCheck.EndpointMonitorId == monitorId)).Should().Be(8);
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var remaining = await database.ExecutionAttempts.Where(item => item.LogicalCheck.EndpointMonitorId == monitorId)
            .Select(item => item.Id).ToArrayAsync();
        remaining.Should().BeEquivalentTo(attempts.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal))
            .Select(pair => pair.Value.Id));
        var work = new Dictionary<string, DurableWork>();
        foreach (var pair in attempts)
        {
            var item = new DurableWork
            {
                Id = Guid.NewGuid(),
                LogicalCheckId = pair.Value.LogicalCheckId,
                WorkKind = MonitorWorkKinds.For(monitor.MonitorType),
                DedupeKey = "retention-" + pair.Key,
                QueueName = "monitoring",
                State = "Completed",
                AvailableAt = pair.Value.StartedAt,
                CreatedAt = pair.Value.StartedAt,
                UpdatedAt = pair.Value.FinishedAt!.Value
            };
            database.DurableWork.Add(item);
            work.Add(pair.Key, item);
        }
        foreach (var state in new[] { "Pending", "Failed", "Completed", "Boundary" })
        {
            var item = new DurableWork
            {
                Id = Guid.NewGuid(),
                LogicalCheckId = attempts["eligible-a"].LogicalCheckId,
                WorkKind = MonitorWorkKinds.For(monitor.MonitorType),
                DedupeKey = "retention-protected-" + state,
                QueueName = "monitoring",
                State = state == "Boundary" ? "Completed" : state,
                AvailableAt = cutoff.AddMinutes(-1),
                CreatedAt = cutoff.AddMinutes(-1),
                UpdatedAt = state == "Boundary" ? cutoff : cutoff.AddTicks(-10),
                LeaseOwnerToken = state == "Completed" ? Guid.NewGuid() : null,
                LeaseAcquiredAt = state == "Completed" ? now : null,
                LeaseExpiresAt = state == "Completed" ? now.AddMinutes(1) : null
            };
            database.DurableWork.Add(item);
            work.Add(state, item);
        }
        await database.SaveChangesAsync();
        (await Batch(false, false).ExecuteWorkAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteWorkAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.DurableWork.CountAsync(item => item.LogicalCheck.EndpointMonitorId == monitorId)).Should().Be(12);
        (await Batch(true, false).ExecuteWorkAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteWorkAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteWorkAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var remainingWork = await database.DurableWork.Where(item => item.LogicalCheck.EndpointMonitorId == monitorId)
            .Select(item => item.Id).ToArrayAsync();
        remainingWork.Should().BeEquivalentTo(work.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal))
            .Select(pair => pair.Value.Id));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var execute = async () => await Batch(true, false).ExecuteAsync(cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class RetentionClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

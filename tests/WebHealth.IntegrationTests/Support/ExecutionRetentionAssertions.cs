using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Seo;

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
        await VerifyRawResultsAsync(database, monitorId, attempts, clock);
        await VerifyLogicalCheckCleanupAsync(database, monitorId, attempts, clock);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var execute = async () => await Batch(true, false).ExecuteAsync(cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task VerifyLogicalCheckCleanupAsync(ApplicationDbContext database, Guid monitorId,
        Dictionary<string, ExecutionAttempt> attempts, TimeProvider clock)
    {
        database.ChangeTracker.Clear();
        var checkId = attempts["eligible-b"].LogicalCheckId;
        LogicalCheckRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, clock, NullLogger<LogicalCheckRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.CheckConfigurationSnapshots.AnyAsync(item => item.LogicalCheckId == checkId)).Should().BeTrue();
        database.SeoObservations.Add(new SeoObservation
        {
            LogicalCheckId = checkId,
            EndpointMonitorId = monitorId,
            Applicability = "NotApplicable",
            NotApplicableReason = "NonHtml",
            ObservedAt = attempts["eligible-b"].FinishedAt!.Value
        });
        await database.SaveChangesAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        await database.SeoObservations.Where(item => item.LogicalCheckId == checkId).ExecuteDeleteAsync();
        database.CertificateObservations.Add(new CertificateObservation
        {
            LogicalCheckId = checkId,
            EndpointMonitorId = monitorId,
            Subject = "CN=execution-retention.test",
            Issuer = "CN=ControlledRetention",
            SerialNumber = "01",
            Sha256Fingerprint = new string('a', 64),
            NotBefore = clock.GetUtcNow().AddYears(-1),
            NotAfter = clock.GetUtcNow().AddYears(1),
            ValidationCategory = "Valid",
            ObservedAt = attempts["eligible-b"].FinishedAt!.Value
        });
        await database.SaveChangesAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        await database.CertificateObservations.Where(item => item.LogicalCheckId == checkId).ExecuteDeleteAsync();
        var attempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = checkId,
            AttemptNumber = 2,
            JobId = "retained-attempt",
            WorkerId = "retention-fixture",
            StartedAt = attempts["eligible-b"].StartedAt,
            FinishedAt = attempts["eligible-b"].FinishedAt,
            InfrastructureOutcome = "Succeeded"
        };
        database.ExecutionAttempts.Add(attempt);
        await database.SaveChangesAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        await database.ExecutionAttempts.Where(item => item.Id == attempt.Id).ExecuteDeleteAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var preserved = attempts.Where(pair => pair.Key != "eligible-b").Select(pair => pair.Value.LogicalCheckId).ToArray();
        (await database.LogicalChecks.Where(item => item.EndpointMonitorId == monitorId).Select(item => item.Id).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        var allIds = attempts.Values.Select(item => item.LogicalCheckId).ToArray();
        (await database.CheckConfigurationSnapshots.Where(item => allIds.Contains(item.LogicalCheckId)).Select(item => item.LogicalCheckId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        (await database.MonitoringDailyAggregates.SingleAsync(item => item.EndpointMonitorId == monitorId)).RawDeletionStartedAt.Should().NotBeNull();
    }

    private static async Task VerifyRawResultsAsync(ApplicationDbContext database, Guid monitorId,
        Dictionary<string, ExecutionAttempt> attempts, TimeProvider clock)
    {
        foreach (var attempt in attempts.Values)
        {
            database.CheckResults.Add(new CheckResult
            {
                LogicalCheckId = attempt.LogicalCheckId,
                EndpointMonitorId = monitorId,
                Outcome = "Healthy",
                MonitorSource = "Manual",
                MeasuredAt = attempt.FinishedAt!.Value,
                CompletedAt = attempt.FinishedAt.Value,
                TotalDurationMs = 100
            });
            database.Findings.Add(new Finding
            {
                Id = Guid.NewGuid(),
                LogicalCheckId = attempt.LogicalCheckId,
                RuleKey = "ControlledRetention",
                IssueKey = "ControlledRetention",
                Severity = "Warning"
            });
            database.RedirectHops.Add(new RedirectHop
            {
                Id = Guid.NewGuid(),
                LogicalCheckId = attempt.LogicalCheckId,
                HopNumber = 1,
                HttpStatus = 301,
                NormalizedFromUrl = "http://execution-retention.test/old",
                NormalizedToUrl = "http://execution-retention.test/status"
            });
        }
        await database.SaveChangesAsync();
        RawResultRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, clock,
            new DailyAggregateWriter(database, clock), NullLogger<RawResultRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.CheckResults.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(8);
        (await database.MonitoringDailyAggregates.AnyAsync(item => item.EndpointMonitorId == monitorId)).Should().BeFalse();
        var observationCheckId = attempts["eligible-b"].LogicalCheckId;
        database.SeoObservations.Add(new SeoObservation
        {
            LogicalCheckId = observationCheckId,
            EndpointMonitorId = monitorId,
            Applicability = "NotApplicable",
            NotApplicableReason = "NonHtml",
            ObservedAt = attempts["eligible-b"].FinishedAt!.Value
        });
        await database.SaveChangesAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        var aggregate = await database.MonitoringDailyAggregates.AsNoTracking().SingleAsync(item => item.EndpointMonitorId == monitorId);
        var dayStart = new DateTimeOffset(aggregate.UtcDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        aggregate.TotalCount.Should().Be(attempts.Values.Count(item => item.FinishedAt >= dayStart && item.FinishedAt < dayStart.AddDays(1)));
        aggregate.RawDeletionStartedAt.Should().Be(clock.GetUtcNow());
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await database.CheckResults.AnyAsync(item => item.LogicalCheckId == observationCheckId)).Should().BeTrue();
        (await database.Findings.CountAsync(item => item.LogicalCheckId == observationCheckId)).Should().Be(1);
        await database.SeoObservations.Where(item => item.LogicalCheckId == observationCheckId).ExecuteDeleteAsync();
        database.CertificateObservations.Add(new CertificateObservation
        {
            LogicalCheckId = observationCheckId,
            EndpointMonitorId = monitorId,
            Subject = "CN=execution-retention.test",
            Issuer = "CN=ControlledRetention",
            SerialNumber = "01",
            Sha256Fingerprint = new string('a', 64),
            NotBefore = clock.GetUtcNow().AddYears(-1),
            NotAfter = clock.GetUtcNow().AddYears(1),
            ValidationCategory = "Valid",
            ObservedAt = attempts["eligible-b"].FinishedAt!.Value
        });
        await database.SaveChangesAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await database.CheckResults.AnyAsync(item => item.LogicalCheckId == observationCheckId)).Should().BeTrue();
        await database.CertificateObservations.Where(item => item.LogicalCheckId == observationCheckId).ExecuteDeleteAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var preserved = attempts.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal))
            .Select(pair => pair.Value.LogicalCheckId).ToArray();
        (await database.CheckResults.Where(item => item.EndpointMonitorId == monitorId).Select(item => item.LogicalCheckId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        var checkIds = attempts.Values.Select(item => item.LogicalCheckId).ToArray();
        (await database.Findings.Where(item => checkIds.Contains(item.LogicalCheckId)).Select(item => item.LogicalCheckId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        (await database.RedirectHops.Where(item => checkIds.Contains(item.LogicalCheckId)).Select(item => item.LogicalCheckId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        var resumed = await database.MonitoringDailyAggregates.AsNoTracking().SingleAsync(item => item.EndpointMonitorId == monitorId);
        resumed.Should().BeEquivalentTo(aggregate);
        (await database.LogicalChecks.CountAsync(item => checkIds.Contains(item.Id))).Should().Be(8);
        (await database.CheckConfigurationSnapshots.CountAsync(item => checkIds.Contains(item.LogicalCheckId))).Should().Be(8);
    }

    private sealed class RetentionClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

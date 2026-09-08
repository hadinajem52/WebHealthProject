using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class PageAuditRetentionAssertions
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
        var clock = new RetentionClock(now);
        var target = new PageAuditTarget
        {
            Id = Guid.NewGuid(),
            EndpointId = monitor.EndpointId,
            Provider = "PageSpeedInsights",
            Category = "Seo",
            Strategy = "Mobile",
            IntervalSeconds = 86400,
            ScheduleAnchor = now,
            NextDueAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };
        database.PageAuditTargets.Add(target);
        var runs = new Dictionary<string, PageAuditRun>();
        foreach (var name in new[] { "eligible-a", "eligible-b", "held", "compare-a", "compare-b", "locale", "evidence", "boundary", "running" })
        {
            var finished = cutoff.AddDays(name switch
            {
                "held" => -8,
                "evidence" => -7,
                "locale" => -6,
                "eligible-a" => -5,
                "eligible-b" => -4,
                "compare-a" => -3,
                "compare-b" => -2,
                "boundary" => 0,
                _ => 1
            });
            var scored = name is "eligible-a" or "compare-a" or "compare-b" or "locale";
            var run = new PageAuditRun
            {
                Id = Guid.NewGuid(),
                BatchId = Guid.NewGuid(),
                PageAuditTargetId = target.Id,
                EndpointId = monitor.EndpointId,
                Source = "Manual",
                InitiatedByUserId = monitor.Endpoint.CreatedByUserId,
                Status = name == "running" ? "Running" : scored ? "CompletedWithWarnings" : "Failed",
                RequestedUrl = monitor.Endpoint.NormalizedUrl,
                Provider = target.Provider,
                Category = target.Category,
                Strategy = target.Strategy,
                Locale = name == "locale" ? "fr-FR" : "en-US",
                LighthouseVersion = scored ? "11.4.0" : null,
                RawScore = scored ? 0.8m : null,
                FailureCategory = scored || name == "running" ? null : "ProviderUnavailable",
                QueuedAt = finished.AddMinutes(-1),
                FinishedAt = name == "running" ? null : finished,
                UpdatedAt = finished,
                LeaseToken = name == "running" ? Guid.NewGuid() : null,
                LeaseExpiresAt = name == "running" ? now.AddMinutes(1) : null
            };
            database.PageAuditRuns.Add(run);
            runs.Add(name, run);
            database.PageAuditItems.Add(new PageAuditItem
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                AuditId = "controlled-retention",
                Status = "Informative"
            });
        }
        database.RetentionHolds.Add(new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = "PageAuditRun",
            ScopeId = runs["held"].Id,
            Reason = "Controlled PageAudit retention",
            CreatedByUserId = monitor.Endpoint.CreatedByUserId,
            CreatedAt = now
        });
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitorId,
            OwnerSubjectId = monitor.Endpoint.OwnerSubjectId ?? monitor.Endpoint.Environment.Website.OwnerSubjectId,
            IssueKey = "PageAudit.Seo",
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
            PageAuditRunId = runs["evidence"].Id,
            EvidenceType = "Opening",
            EvidenceRole = "ControlledRetention",
            BoundedSnapshot = "{}",
            CapturedAt = now
        });
        await database.SaveChangesAsync();
        PageAuditRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, clock, NullLogger<PageAuditRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.PageAuditRuns.CountAsync(run => run.PageAuditTargetId == target.Id)).Should().Be(9);
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        clock.Now = now.AddDays(1);
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var preserved = runs.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal)).Select(pair => pair.Value.Id).ToArray();
        (await database.PageAuditRuns.Where(run => run.PageAuditTargetId == target.Id).Select(run => run.Id).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        var allIds = runs.Values.Select(run => run.Id).ToArray();
        (await database.PageAuditItems.Where(item => allIds.Contains(item.RunId)).Select(item => item.RunId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        (await database.PageAuditTargets.AnyAsync(item => item.Id == target.Id)).Should().BeTrue();
        (await database.IncidentEvidence.CountAsync(item => item.IncidentId == incident.Id)).Should().Be(1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var execute = async () => await Batch(true, false).ExecuteAsync(cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class RetentionClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class RetentionScopeAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint.Environment.Website)
            .SingleAsync(item => item.Id == monitorId);
        var endpoint = monitor.Endpoint;
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var check = new LogicalCheck
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitorId,
            Source = "Manual",
            RequestedAt = now,
            InitiatedByUserId = endpoint.CreatedByUserId,
            State = "Queued",
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = now,
            QueuedAt = now
        };
        database.LogicalChecks.Add(check);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitorId,
            OwnerSubjectId = endpoint.OwnerSubjectId ?? endpoint.Environment.Website.OwnerSubjectId,
            IssueKey = "Http.ServerError",
            Severity = "Critical",
            Status = "Open",
            OpenedAt = now,
            Version = 1
        };
        database.Incidents.Add(incident);
        var crawl = new CrawlRun
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            Status = "Completed",
            StopReason = "FrontierExhausted",
            SeedUrls = endpoint.NormalizedUrl,
            QueryPolicy = "Canonicalize",
            MaxPages = 10,
            MaxDepth = 2,
            RobotsOverrideRefusedBecause = "NotRequested",
            StartedAt = now,
            FinishedAt = now
        };
        database.CrawlRuns.Add(crawl);
        var target = new PageAuditTarget
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
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
        var page = new PageAuditRun
        {
            Id = Guid.NewGuid(),
            PageAuditTargetId = target.Id,
            EndpointId = endpoint.Id,
            Source = "Scheduled",
            Status = "Completed",
            RequestedUrl = endpoint.NormalizedUrl,
            Provider = target.Provider,
            Category = target.Category,
            Strategy = target.Strategy,
            Locale = "en-US",
            LighthouseVersion = "11.4.0",
            RawScore = 0.8m,
            QueuedAt = now,
            AnalysisAt = now,
            FinishedAt = now,
            UpdatedAt = now
        };
        database.PageAuditRuns.Add(page);
        await database.SaveChangesAsync();
        foreach (var source in new[] { true, false })
            database.IncidentEvidence.Add(new IncidentEvidence
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                EndpointMonitorId = monitorId,
                LogicalCheckId = source ? check.Id : null,
                PageAuditRunId = source ? null : page.Id,
                EvidenceType = "Opening",
                EvidenceRole = "ControlledHoldEvidence",
                BoundedSnapshot = "{}",
                CapturedAt = now
            });
        await database.SaveChangesAsync();
        var scopes = new Dictionary<string, Guid>
        {
            ["Client"] = endpoint.Environment.Website.ClientId,
            ["Website"] = endpoint.Environment.WebsiteId,
            ["Environment"] = endpoint.EnvironmentId,
            ["Endpoint"] = endpoint.Id,
            ["Monitor"] = monitorId,
            ["LogicalCheck"] = check.Id,
            ["Incident"] = incident.Id,
            ["CrawlRun"] = crawl.Id,
            ["PageAuditRun"] = page.Id
        };
        foreach (var scope in scopes)
        {
            var hold = new RetentionHold
            {
                Id = Guid.NewGuid(),
                ScopeType = scope.Key,
                ScopeId = scope.Value,
                Reason = "Controlled scope expansion",
                CreatedByUserId = endpoint.CreatedByUserId,
                CreatedAt = now,
                ExpiresAt = now.AddTicks(10)
            };
            database.RetentionHolds.Add(hold);
            await database.SaveChangesAsync();
            var queries = new RetentionHoldQueries(database, now);
            (await queries.RelatedEndpointIds().ContainsAsync(endpoint.Id)).Should().BeTrue(scope.Key);
            var ancestor = scope.Key is "Client" or "Website" or "Environment" or "Endpoint";
            (await queries.EndpointIds().ContainsAsync(endpoint.Id)).Should().Be(ancestor, scope.Key);
            (await queries.MonitorIds().ContainsAsync(monitorId)).Should().Be(ancestor || scope.Key == "Monitor", scope.Key);
            (await queries.CrawlRunIds().ContainsAsync(crawl.Id)).Should().Be(ancestor || scope.Key == "CrawlRun", scope.Key);
            (await queries.IncidentIds().ContainsAsync(incident.Id)).Should().Be(scope.Key != "CrawlRun", scope.Key);
            (await queries.LogicalCheckIds().ContainsAsync(check.Id)).Should().Be(scope.Key != "CrawlRun", scope.Key);
            (await queries.PageAuditRunIds().ContainsAsync(page.Id)).Should().Be(scope.Key != "CrawlRun", scope.Key);
            (await new RetentionHoldQueries(database, now.AddTicks(10)).LogicalCheckIds().ContainsAsync(check.Id))
                .Should().BeFalse("expiry is exclusive at the exact microsecond");
            hold.ReleasedAt = now;
            hold.ReleasedByUserId = endpoint.CreatedByUserId;
            await database.SaveChangesAsync();
            (await queries.IncidentIds().ContainsAsync(incident.Id)).Should().BeFalse("release takes effect immediately");
            (await queries.RelatedEndpointIds().ContainsAsync(endpoint.Id)).Should().BeFalse("release takes effect immediately");
        }
        await transaction.RollbackAsync();
    }
}

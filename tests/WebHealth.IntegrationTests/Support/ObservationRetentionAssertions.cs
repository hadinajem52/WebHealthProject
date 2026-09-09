using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Seo;

namespace WebHealth.IntegrationTests.Support;

internal static class ObservationRetentionAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId, bool certificate)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(builder, connectionString);
        await using var database = new ApplicationDbContext(builder.Options);
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint.Environment.Website).SingleAsync(item => item.Id == monitorId);
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var clock = new RetentionClock(now);
        var cutoff = certificate ? now.AddMonths(-24) : now.AddDays(-90);
        var ids = new Dictionary<string, Guid>();
        foreach (var name in new[] { "eligible-a", "eligible-b", "held", "current-a", "current-b", "boundary", "after" })
        {
            var observed = name switch
            {
                "boundary" => cutoff,
                "after" => cutoff.AddTicks(10),
                "current-a" or "current-b" => cutoff.AddDays(-1),
                _ => cutoff.AddDays(-2)
            };
            var check = new LogicalCheck
            {
                Id = Guid.NewGuid(),
                EndpointMonitorId = monitorId,
                Source = "Manual",
                RequestedAt = observed.AddMinutes(-1),
                InitiatedByUserId = monitor.Endpoint.CreatedByUserId,
                State = "Completed",
                PolicyFingerprint = monitor.ConfigurationFingerprint,
                CreatedAt = observed.AddMinutes(-1),
                QueuedAt = observed.AddMinutes(-1),
                StartedAt = observed.AddMinutes(-1),
                CompletedAt = observed
            };
            ids.Add(name, check.Id);
            database.LogicalChecks.Add(check);
            database.CheckConfigurationSnapshots.Add(CheckConfigurationSnapshotFactory.Create(monitor, check.Id,
                check.CreatedAt, NullLogger.Instance));
            database.CheckResults.Add(new CheckResult
            {
                LogicalCheckId = check.Id,
                EndpointMonitorId = monitorId,
                Outcome = "Healthy",
                MonitorSource = "Manual",
                ConfigurationIdentity = MonitoringConfigurationIdentity.Format(
                    monitor.ConfigurationFingerprint, 2, monitor.CurrentTruthGeneration),
                MeasuredAt = observed,
                CompletedAt = observed,
                CurrentStateDisposition = name is "boundary" or "after" ? "Superseded" : "Current"
            });
            if (certificate)
            {
                database.CertificateObservations.Add(new CertificateObservation
                {
                    LogicalCheckId = check.Id,
                    EndpointMonitorId = monitorId,
                    Subject = "CN=observation-retention.test",
                    Issuer = "CN=ControlledRetention",
                    SerialNumber = "01",
                    Sha256Fingerprint = new string('a', 64),
                    NotBefore = observed.AddDays(-1),
                    NotAfter = observed.AddYears(1),
                    ValidationCategory = "Valid",
                    ObservedAt = observed
                });
            }
            else
            {
                database.SeoObservations.Add(new SeoObservation
                {
                    LogicalCheckId = check.Id,
                    EndpointMonitorId = monitorId,
                    Applicability = "NotApplicable",
                    NotApplicableReason = "NonHtml",
                    ObservedAt = observed
                });
            }
        }
        database.RetentionHolds.Add(new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = "LogicalCheck",
            ScopeId = ids["held"],
            Reason = "Controlled observation retention",
            CreatedByUserId = monitor.Endpoint.CreatedByUserId,
            CreatedAt = now
        });
        await database.SaveChangesAsync();
        Task<RetentionBatchResult> Execute(bool enabled, bool dryRun, CancellationToken token = default)
        {
            var batch = new ObservationRetentionBatch(database, new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 },
                clock, NullLogger<ObservationRetentionBatch>.Instance);
            return certificate ? batch.ExecuteCertificateAsync(token) : batch.ExecuteSeoAsync(token);
        }
        IQueryable<Guid> Remaining() => certificate
            ? database.CertificateObservations.Where(item => item.EndpointMonitorId == monitorId).Select(item => item.LogicalCheckId)
            : database.SeoObservations.Where(item => item.EndpointMonitorId == monitorId).Select(item => item.LogicalCheckId);
        (await Execute(false, false)).Should().Be(new RetentionBatchResult(0, 0));
        (await Execute(true, true)).Should().Be(new RetentionBatchResult(1, 0));
        (await Remaining().CountAsync()).Should().Be(7);
        (await Execute(true, false)).Should().Be(new RetentionBatchResult(1, 1));
        (await Execute(true, false)).Should().Be(new RetentionBatchResult(1, 1));
        (await Execute(true, false)).Should().Be(new RetentionBatchResult(0, 0));
        (await Remaining().ToArrayAsync()).Should().BeEquivalentTo(ids.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal))
            .Select(pair => pair.Value));
        clock.Now = now.AddDays(1);
        (await Execute(true, false)).Should().Be(new RetentionBatchResult(1, 1));
        (await Execute(true, false)).Should().Be(new RetentionBatchResult(0, 0));
        (await Remaining().ToArrayAsync()).Should().BeEquivalentTo(new[] { ids["held"], ids["current-a"], ids["current-b"], ids["after"] });
        (await database.CheckResults.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(7);
        (await database.LogicalChecks.CountAsync(item => item.EndpointMonitorId == monitorId)).Should().Be(7);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var execute = async () => await Execute(true, false, cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class RetentionClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Seo;

namespace WebHealth.IntegrationTests.Support;

internal static class RobotsRetentionAssertions
{
    public static async Task VerifyAsync(string connectionString, Guid monitorId)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(builder, connectionString);
        await using var database = new ApplicationDbContext(builder.Options);
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint).SingleAsync(item => item.Id == monitorId);
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var cutoff = now.AddDays(-90);
        var origins = new Dictionary<string, string>
        {
            ["eligible-a"] = "http://robots-retention.test.evil",
            ["eligible-b"] = "http://robots-retention-other.test",
            ["held"] = RobotsRefreshService.OriginOf(monitor.Endpoint.NormalizedUrl),
            ["fresh"] = "http://robots-retention-fresh.test",
            ["boundary"] = "http://robots-retention-boundary.test",
            ["sitemap"] = "http://robots-retention-sitemap.test",
            ["required"] = "http://robots-retention-required.test",
            ["exception"] = "http://robots-retention-exception.test"
        };
        foreach (var pair in origins)
        {
            var fetched = pair.Key == "boundary" ? cutoff : cutoff.AddTicks(-10);
            database.RobotsSnapshots.Add(new RobotsSnapshot
            {
                Origin = pair.Value,
                Host = new Uri(pair.Value).Host,
                Port = 80,
                Status = "NotFound",
                HttpStatus = 404,
                FetchedAt = fetched,
                UpdatedAt = fetched,
                ExpiresAt = pair.Key == "fresh" ? now.AddTicks(10) : pair.Key == "eligible-a" ? now : fetched.AddHours(1),
                SitemapRequired = pair.Key == "required",
                ConfiguredSitemapUrl = pair.Key == "sitemap" ? pair.Value + "/sitemap.xml" : null,
                ExceptionReason = pair.Key == "exception" ? "Controlled policy preservation" : null,
                ExceptionApprovedByUserId = pair.Key == "exception" ? monitor.Endpoint.CreatedByUserId : null,
                ExceptionApprovedAt = pair.Key == "exception" ? fetched : null,
                Version = 1
            });
        }
        var hold = new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = "Endpoint",
            ScopeId = monitor.EndpointId,
            Reason = "Controlled robots retention",
            CreatedByUserId = monitor.Endpoint.CreatedByUserId,
            CreatedAt = now
        };
        database.RetentionHolds.Add(hold);
        await database.SaveChangesAsync();
        RobotsRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, new RetentionClock(now), NullLogger<RobotsRetentionBatch>.Instance);
        var allOrigins = origins.Values.ToArray();
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.RobotsSnapshots.CountAsync(item => allOrigins.Contains(item.Origin))).Should().Be(8);
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await database.RobotsSnapshots.Where(item => allOrigins.Contains(item.Origin)).Select(item => item.Origin).ToArrayAsync())
            .Should().BeEquivalentTo(origins.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal)).Select(pair => pair.Value));
        hold.ReleasedAt = now;
        hold.ReleasedByUserId = monitor.Endpoint.CreatedByUserId;
        await database.SaveChangesAsync();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await database.RobotsSnapshots.AnyAsync(item => item.Origin == origins["held"])).Should().BeFalse();
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class CrawlRetentionAssertions
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
        var runs = new Dictionary<string, CrawlRun>();
        foreach (var name in new[] { "eligible-a", "eligible-b", "held", "compare-a", "compare-b", "latest", "boundary", "running" })
        {
            var started = cutoff.AddDays(name switch
            {
                "boundary" => -10,
                "held" => -6,
                "eligible-a" => -5,
                "eligible-b" => -4,
                "compare-a" => -3,
                "compare-b" => -2,
                "latest" => -1,
                _ => 1
            });
            var comparable = name is "eligible-a" or "compare-a" or "compare-b";
            var run = new CrawlRun
            {
                Id = Guid.NewGuid(),
                EndpointId = monitor.EndpointId,
                Status = name == "running" ? "Running" : comparable ? "Completed" : "Failed",
                StopReason = comparable ? "FrontierExhausted" : "Failed",
                SeedUrls = "[\"http://crawl-retention.test/status\"]",
                QueryPolicy = "Canonicalize",
                MaxPages = 10,
                MaxDepth = 1,
                PagesFetched = comparable ? 1 : 0,
                LinksRecorded = 1,
                RobotsOverrideRefusedBecause = "NotRequested",
                StartedAt = started,
                FinishedAt = name == "running" ? null : name == "boundary" ? cutoff : started.AddMinutes(1),
                ExecutionClaimId = Guid.NewGuid()
            };
            database.CrawlRuns.Add(run);
            runs.Add(name, run);
            var target = "http://crawl-retention.test/" + name;
            database.CrawlLinkResults.Add(new CrawlLinkResult
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                TargetUrl = target,
                TargetUrlHash = CrawlResultSink.Hash(target),
                Classification = "Broken",
                StatusCode = 404,
                RecordedAt = started
            });
        }
        database.RetentionHolds.Add(new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = "CrawlRun",
            ScopeId = runs["held"].Id,
            Reason = "Controlled crawl retention",
            CreatedByUserId = monitor.Endpoint.CreatedByUserId,
            CreatedAt = now
        });
        await database.SaveChangesAsync();
        CrawlRetentionBatch Batch(bool enabled, bool dryRun) => new(database,
            new() { Enabled = enabled, DryRun = dryRun, BatchSize = 1 }, new RetentionClock(now), NullLogger<CrawlRetentionBatch>.Instance);
        (await Batch(false, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        (await Batch(true, true).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 0));
        (await database.CrawlRuns.CountAsync(item => item.EndpointId == monitor.EndpointId)).Should().Be(8);
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(1, 1));
        (await Batch(true, false).ExecuteAsync()).Should().Be(new RetentionBatchResult(0, 0));
        var preserved = runs.Where(pair => !pair.Key.StartsWith("eligible-", StringComparison.Ordinal)).Select(pair => pair.Value.Id).ToArray();
        (await database.CrawlRuns.Where(item => item.EndpointId == monitor.EndpointId).Select(item => item.Id).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        var allIds = runs.Values.Select(run => run.Id).ToArray();
        (await database.CrawlLinkResults.Where(item => allIds.Contains(item.RunId)).Select(item => item.RunId).ToArrayAsync())
            .Should().BeEquivalentTo(preserved);
        var baseline = await CrawlHistoryQueries.Comparable(database.CrawlRuns).Where(run => run.EndpointId == monitor.EndpointId)
            .OrderByDescending(run => run.StartedAt).ThenByDescending(run => run.Id).Take(2).Select(run => run.Id).ToArrayAsync();
        baseline.Should().Equal(runs["compare-b"].Id, runs["compare-a"].Id);
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

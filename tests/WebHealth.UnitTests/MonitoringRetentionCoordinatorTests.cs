using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MonitoringRetentionCoordinatorTests
{
    [Fact]
    public async Task DisabledRunDoesNoWork()
    {
        var runner = new Runner((_, _) => Task.FromResult((1, 1)));
        var result = await new MonitoringRetentionCoordinator(runner, new(), TimeProvider.System).ExecuteAsync();
        result.Batches.Should().BeEmpty();
        runner.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRunSamplesEachCategoryOnce()
    {
        var runner = new Runner((_, _) => Task.FromResult((1000, 0)));
        var result = await new MonitoringRetentionCoordinator(runner, new() { Enabled = true }, TimeProvider.System).ExecuteAsync();
        result.Batches.Select(item => item.Category).Should().Equal(Enum.GetValues<RetentionCategory>());
        result.Batches.Sum(item => item.Selected).Should().Be(Enum.GetValues<RetentionCategory>().Length * 1000);
        result.Batches.Sum(item => item.Deleted).Should().Be(0);
    }

    [Fact]
    public async Task DeletionVisitsEveryCategoryBeforeRepeatingAndHonorsBatchCap()
    {
        var runner = new Runner((_, _) => Task.FromResult((1, 1)));
        var options = new MonitoringRetentionOptions { Enabled = true, DryRun = false };
        var result = await new MonitoringRetentionCoordinator(runner, options, TimeProvider.System).ExecuteAsync();
        result.Batches.Should().HaveCount(options.MaximumBatchesPerRun);
        var categories = Enum.GetValues<RetentionCategory>();
        result.Batches.Select(item => item.Category).Should().Equal(categories.Concat(categories).Take(options.MaximumBatchesPerRun));
    }

    [Fact]
    public async Task EmptyPassStopsWithoutRepeatedQueries()
    {
        var runner = new Runner((_, _) => Task.FromResult((0, 0)));
        var result = await new MonitoringRetentionCoordinator(runner, new() { Enabled = true, DryRun = false }, TimeProvider.System).ExecuteAsync();
        result.Batches.Should().HaveCount(Enum.GetValues<RetentionCategory>().Length);
    }

    [Fact]
    public async Task SmallBatchCapStopsWithinFirstPass()
    {
        var runner = new Runner((_, _) => Task.FromResult((1, 1)));
        var options = new MonitoringRetentionOptions { Enabled = true, DryRun = false, MaximumBatchesPerRun = 2 };
        var result = await new MonitoringRetentionCoordinator(runner, options, TimeProvider.System).ExecuteAsync();
        result.Batches.Select(item => item.Category).Should().Equal(RetentionCategory.DailyAggregatePreparation, RetentionCategory.IncidentBundles);
    }

    [Fact]
    public async Task ElapsedBudgetPreventsAnotherBatch()
    {
        var clock = new Clock();
        var runner = new Runner((_, _) =>
        {
            clock.Timestamp += TimeSpan.FromSeconds(30).Ticks;
            return Task.FromResult((1, 1));
        });
        var result = await new MonitoringRetentionCoordinator(runner, new() { Enabled = true, DryRun = false }, clock).ExecuteAsync();
        result.Batches.Should().HaveCount(1);
        result.DurationLimitReached.Should().BeTrue();
    }

    [Fact]
    public async Task DeadlineCancelsAnInFlightBatch()
    {
        var runner = new Runner(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return (1, 1);
        });
        var options = new MonitoringRetentionOptions { Enabled = true, MaximumRunDuration = TimeSpan.FromSeconds(1) };
        var result = await new MonitoringRetentionCoordinator(runner, options, TimeProvider.System).ExecuteAsync();
        result.DurationLimitReached.Should().BeTrue();
        result.Batches.Should().BeEmpty();
        runner.Categories.Should().HaveCount(1);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var runner = new Runner((_, _) => Task.FromResult((1, 1)));
        var coordinator = new MonitoringRetentionCoordinator(runner, new() { Enabled = true }, TimeProvider.System);
        var execute = async () => await coordinator.ExecuteAsync(cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
        runner.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task JobFailureDoesNotExposeUnderlyingDiagnostic()
    {
        var runner = new Runner((_, _) => throw new InvalidOperationException("sensitive database detail"));
        var options = new MonitoringRetentionOptions { Enabled = true };
        var coordinator = new MonitoringRetentionCoordinator(runner, options, TimeProvider.System);
        var job = new MonitoringRetentionJob(coordinator, options, NullLogger<MonitoringRetentionJob>.Instance);
        var execute = async () => await job.ExecuteAsync(CancellationToken.None);
        var error = (await execute.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Be("Monitoring retention failed: InvalidOperationException");
        error.InnerException.Should().BeNull();
    }

    private sealed class Runner(Func<RetentionCategory, CancellationToken, Task<(int, int)>> execute) : IMonitoringRetentionBatchRunner
    {
        public List<RetentionCategory> Categories { get; } = [];
        public Task<(int Selected, int Deleted)> ExecuteAsync(RetentionCategory category, CancellationToken cancellationToken)
        {
            Categories.Add(category);
            return execute(category, cancellationToken);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
    }
}

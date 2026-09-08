using FluentAssertions;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MonitoringRetentionOptionsTests
{
    [Fact]
    public void DefaultsKeepDeletionDisabledAndBoundRuns()
    {
        var options = new MonitoringRetentionOptions();
        options.Enabled.Should().BeFalse();
        options.DryRun.Should().BeTrue();
        options.BatchSize.Should().Be(1000);
        options.MaximumBatchesPerRun.Should().Be(20);
        options.MaximumRunDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.Validate();
    }

    [Theory]
    [InlineData(0, 20, 30)]
    [InlineData(1001, 20, 30)]
    [InlineData(1000, 0, 30)]
    [InlineData(1000, 21, 30)]
    [InlineData(1000, 20, 0)]
    [InlineData(1000, 20, 31)]
    public void UnsafeRunLimitsAreRejected(int batch, int batches, int seconds)
    {
        var options = new MonitoringRetentionOptions
        {
            BatchSize = batch,
            MaximumBatchesPerRun = batches,
            MaximumRunDuration = TimeSpan.FromSeconds(seconds)
        };
        var validate = options.Validate;
        validate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SmallestRunLimitsAreSupported() => new MonitoringRetentionOptions
    {
        BatchSize = 1,
        MaximumBatchesPerRun = 1,
        MaximumRunDuration = TimeSpan.FromSeconds(1)
    }.Validate();
}

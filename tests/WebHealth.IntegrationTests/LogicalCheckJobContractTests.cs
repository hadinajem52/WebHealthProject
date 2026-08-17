using FluentAssertions;
using Hangfire;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class LogicalCheckJobContractTests
{
    [Fact]
    public void ExecuteAsync_UsesTheMonitoringQueueAndBoundedRetries()
    {
        var method = typeof(LogicalCheckJob).GetMethod(nameof(LogicalCheckJob.ExecuteAsync));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue.Should().Be("monitoring");
        var retry = method.GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .Cast<AutomaticRetryAttribute>().Single();
        retry.Attempts.Should().Be(2);
        retry.OnAttemptsExceeded.Should().Be(AttemptsExceededAction.Fail);
    }
}

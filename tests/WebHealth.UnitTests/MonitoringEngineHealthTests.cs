using FluentAssertions;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class MonitoringEngineHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitoringWorkerStatus Workers = new(true, true, true, Now);

    [Theory]
    [InlineData(179, "Healthy")]
    [InlineData(180, "Warning")]
    [InlineData(300, "Critical")]
    public void SchedulerSuccessAgeUsesExactThresholds(int seconds, string expected) =>
        Evaluate(Operations(Now.AddSeconds(-seconds))).Status.Should().Be(expected);

    [Theory]
    [InlineData(1, "Warning")]
    [InlineData(3, "Critical")]
    public void ConsecutiveFailuresAffectHealthEvenWithRecentSuccess(int failures, string expected) =>
        Evaluate(Operations(Now).Select(item => item with { ConsecutiveFailures = failures }).ToArray()).Status.Should().Be(expected);

    [Theory]
    [InlineData(299, "Healthy")]
    [InlineData(300, "Warning")]
    [InlineData(900, "Critical")]
    public void QueueAgeUsesExactThresholds(int seconds, string expected) =>
        Evaluate(Operations(Now), queued: Now.AddSeconds(-seconds)).Status.Should().Be(expected);

    [Theory]
    [InlineData(600, "Healthy")]
    [InlineData(601, "Warning")]
    [InlineData(1800, "Critical")]
    public void OverdueAgeHonorsDispatchGrace(int seconds, string expected) =>
        Evaluate(Operations(Now), due: Now.AddSeconds(-seconds)).Status.Should().Be(expected);

    [Theory]
    [InlineData(false, true, true, 0, "WorkerStatusUnavailable")]
    [InlineData(true, false, false, 0, "WorkerAbsent")]
    [InlineData(true, true, false, 0, "ShortCheckQueueUncovered")]
    [InlineData(true, true, true, 121, "WorkerHeartbeatStale")]
    public void MissingWorkerCapabilityIsCritical(bool available, bool present, bool covered, int age, string reason)
    {
        var result = Evaluate(Operations(Now), new(available, present, covered, Now.AddSeconds(-age)));
        result.Status.Should().Be("Critical");
        result.Reasons.Should().Contain(reason);
    }

    [Fact]
    public void HeartbeatAtToleranceRemainsHealthy() =>
        Evaluate(Operations(Now), Workers with { LastHeartbeatAt = Now.AddMinutes(-2) }).Status.Should().Be("Healthy");

    [Fact]
    public void NeverStartedSchedulerIsCritical() => Evaluate([]).Status.Should().Be("Critical");

    [Fact]
    public void DisabledSchedulingOverridesRuntimeFaults()
    {
        var result = MonitoringEngineHealth.Evaluate(false, [], new(false, false, false, null),
            Now.AddDays(-1), Now.AddDays(-1), TimeSpan.FromMinutes(10), Now);
        result.Status.Should().Be("Healthy");
        result.Reasons.Should().Equal("DisabledByConfiguration");
    }

    private static MonitoringEngineHealth Evaluate(IReadOnlyList<MonitoringOperationStatus> operations,
        MonitoringWorkerStatus? workers = null, DateTimeOffset? queued = null, DateTimeOffset? due = null) =>
        MonitoringEngineHealth.Evaluate(true, operations, workers ?? Workers, queued, due, TimeSpan.FromMinutes(10), Now);

    private static MonitoringOperationStatus[] Operations(DateTimeOffset success) =>
        [new("monitoring-dispatch", success, success, null, 0, null, 0),
         new("monitoring-reconciliation", success, success, null, 0, null, 0)];
}

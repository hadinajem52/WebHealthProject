using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.PngAudits;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.PngAudits;
using WebHealth.Infrastructure.SiteAnalysis;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngAuditExecutionInfrastructureTests
{
    [Fact]
    public void PngAuditRunJob_UsesTheDedicatedQueueWithoutHangfireRetries()
    {
        var method = typeof(PngAuditRunJob).GetMethod(nameof(PngAuditRunJob.ExecuteAsync));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue.Should().Be(PngAuditQueueNames.ImageAudits);
        method.GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .Cast<AutomaticRetryAttribute>().Single().Attempts.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateJobDelivery_StopsWhenTheRunCannotBeClaimed()
    {
        var sink = new UnclaimableSink();
        var execution = new PngAuditExecutionService(
            sink,
            null!,
            null!,
            null!,
            null!,
            new PngAuditOptions(),
            TimeProvider.System,
            NullLogger<PngAuditExecutionService>.Instance);

        await execution.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        sink.ClaimAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Reconciliation_ReenqueuesEveryRecoverableRun()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var queue = new RecordingQueue();
        var job = new PngAuditReconciliationJob(
            new StubReconciler(first, second),
            queue,
            NullLogger<PngAuditReconciliationJob>.Instance);

        await job.ReconcileAsync(CancellationToken.None);

        queue.RunIds.Should().Equal(first, second);
    }

    [Fact]
    public async Task PngImageWork_UsesOneChildSlotAndLeavesSharedCapacityForCrawling()
    {
        using var imageGate = new PngImageRequestGate();
        var budget = new SiteAnalysisRequestBudget(new SafeHttpTransportOptions());
        using var firstImage = await imageGate.AcquireAsync(CancellationToken.None);
        using var imageBudget = await budget.AcquireAsync(CancellationToken.None);

        var secondImage = imageGate.AcquireAsync(CancellationToken.None).AsTask();
        secondImage.IsCompleted.Should().BeFalse();
        using var crawlBudget = await budget.AcquireAsync(CancellationToken.None);

        budget.Capacity.Should().BeGreaterThan(1);
        budget.Capacity.Should().BeLessOrEqualTo(new SafeHttpTransportOptions().GlobalConcurrency / 2);
        firstImage.Dispose();
        using var admittedImage = await secondImage;
    }

    private sealed class RecordingQueue : IPngAuditRunQueue
    {
        public List<Guid> RunIds { get; } = [];

        public void Enqueue(Guid runId) => RunIds.Add(runId);
    }

    private sealed class StubReconciler(params Guid[] runIds) : IPngAuditReconciler
    {
        public Task<IReadOnlyList<Guid>> ReconcileAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(runIds);
    }

    private sealed class UnclaimableSink : IPngAuditResultSink
    {
        public int ClaimAttempts { get; private set; }

        public Task CreateQueuedRunAsync(
            PngAuditQueuedRun run,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PngAuditRunClaim?> TryClaimAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
        {
            ClaimAttempts++;
            return Task.FromResult<PngAuditRunClaim?>(null);
        }

        public Task<bool> HeartbeatAsync(
            Guid runId,
            Guid leaseToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateCrawlProgressAsync(
            Guid runId,
            Guid leaseToken,
            PngAuditCrawlProgress progress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RecordBatchAsync(
            Guid runId,
            Guid leaseToken,
            PngAuditResultBatch batch,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CompleteAsync(
            Guid runId,
            Guid leaseToken,
            PngAuditRunTotals totals,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> FailAsync(
            Guid runId,
            Guid? leaseToken,
            string status,
            string failureCode,
            string? safeDiagnostic,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

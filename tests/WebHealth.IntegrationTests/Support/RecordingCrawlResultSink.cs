using System.Collections.Concurrent;
using WebHealth.Application.Crawling;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// Holds a run's results in memory, for the execution tests. It lives in the test project rather
/// than beside the real sink so it cannot be registered by accident: crawling is enabled by
/// default, and a non-durable sink reachable from production configuration would mean results that
/// vanish when the job's scope ends.
/// </summary>
internal sealed class RecordingCrawlResultSink : ICrawlResultSink
{
    private readonly ConcurrentQueue<CrawlLinkRecord> _links = new();

    public IReadOnlyCollection<CrawlLinkRecord> Links => [.. _links];

    public CrawlRunOutcome? Outcome { get; private set; }

    public CrawlRunStart? Start { get; private set; }

    /// <summary>The claim the execution took, so a test can see the run was owned before it ran.</summary>
    public Guid? ExecutionClaimId { get; private set; }

    /// <summary>Set to refuse the claim, as a redelivered job's sink would.</summary>
    public bool RefuseClaim { get; set; }

    public Task BeginRunAsync(CrawlRunStart start, CancellationToken cancellationToken = default)
    {
        Start = start;
        return Task.CompletedTask;
    }

    public Task RecordLinkAsync(CrawlLinkRecord record, CancellationToken cancellationToken = default)
    {
        _links.Enqueue(record);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimRunAsync(
        Guid runId,
        Guid executionClaimId,
        CancellationToken cancellationToken = default)
    {
        if (RefuseClaim) return Task.FromResult(false);
        ExecutionClaimId = executionClaimId;
        return Task.FromResult(true);
    }

    public Task<bool> RecordRunOutcomeAsync(
        CrawlRunOutcome outcome,
        Guid executionClaimId,
        CancellationToken cancellationToken = default)
    {
        if (executionClaimId != ExecutionClaimId) return Task.FromResult(false);
        Outcome = outcome;
        return Task.FromResult(true);
    }
}

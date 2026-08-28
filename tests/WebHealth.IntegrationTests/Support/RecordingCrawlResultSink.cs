using System.Collections.Concurrent;
using WebHealth.Application.Crawling;

namespace WebHealth.IntegrationTests.Support;

internal sealed class RecordingCrawlResultSink : ICrawlResultSink
{
    private readonly ConcurrentQueue<CrawlLinkRecord> _links = new();

    public IReadOnlyCollection<CrawlLinkRecord> Links => [.. _links];

    public CrawlRunOutcome? Outcome { get; private set; }

    public CrawlRunStart? Start { get; private set; }

    public Guid? ExecutionClaimId { get; private set; }

    public (int PagesFetched, int LinksRecorded)? Progress { get; private set; }

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

    public Task<int> RecordLinksAsync(
        IReadOnlyList<CrawlLinkRecord> records,
        CancellationToken cancellationToken = default)
    {
        foreach (var record in records) _links.Enqueue(record);
        return Task.FromResult(_links.Count);
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

    public Task<bool> UpdateProgressAsync(
        Guid runId,
        Guid executionClaimId,
        int pagesFetched,
        int linksRecorded,
        CancellationToken cancellationToken = default)
    {
        if (executionClaimId != ExecutionClaimId) return Task.FromResult(false);
        Progress = (pagesFetched, linksRecorded);
        return Task.FromResult(true);
    }
}

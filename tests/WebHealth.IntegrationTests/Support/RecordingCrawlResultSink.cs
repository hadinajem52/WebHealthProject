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

    public Task RecordRunOutcomeAsync(CrawlRunOutcome outcome, CancellationToken cancellationToken = default)
    {
        Outcome = outcome;
        return Task.CompletedTask;
    }
}

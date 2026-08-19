using System.Collections.Concurrent;
using WebHealth.Application.Crawling;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// Holds a run's results in memory. Phase 6.7 owns the crawl schema and replaces this with the
/// persistent sink; until then this is what the execution tests assert against, and it keeps the
/// execution loop honest about writing results as they resolve rather than at the end.
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

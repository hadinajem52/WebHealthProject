namespace WebHealth.Infrastructure.Crawling;

public sealed record CrawlSchedulingOptions
{
    public const string SectionName = "Crawling:Scheduling";

    public bool Enabled { get; init; }

    /// <summary>
    /// Workers reserved for the <c>crawl</c> queue, on a Hangfire server that serves no other
    /// queue. Small on purpose: a crawl is a long job, and its concurrency inside a run comes from
    /// <see cref="RequestConcurrency" /> rather than from running many runs at once.
    /// </summary>
    public int WorkerCount { get; init; } = 1;

    /// <summary>
    /// Requests in flight for one run. Validation refuses more than half the transport's global
    /// concurrency, so a saturated crawl can never take the whole shared budget from monitoring.
    /// </summary>
    public int RequestConcurrency { get; init; } = 2;

    /// <summary>The specification's default: 2 requests per second per host.</summary>
    public double RequestsPerSecondPerHost { get; init; } = 2;

    /// <summary>BR-L05. A run that reaches this stops with <c>DurationLimit</c>.</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(30);

    public int FetchTimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// Enough to read the links out of a large page, and far below the transport's own cap. A
    /// crawl reads many more bodies than a check does, so its bound is tighter, not looser.
    /// </summary>
    public int MaxPageBytes { get; init; } = 1024 * 1024;
}

internal static class CrawlQueueNames
{
    public const string Crawl = "crawl";
}

namespace WebHealth.Infrastructure.PageAudits;

public sealed record PageAuditSchedulingOptions
{
    public const string SectionName = "PageAudits:Scheduling";

    public bool Enabled { get; init; }

    /// <summary>
    /// Workers reserved for the <c>page-audits</c> queue, on a Hangfire server that serves no
    /// other queue. One by default: the work is a single outbound call per run against a quota
    /// somebody else owns, so concurrency buys latency at the cost of spending that quota faster.
    /// </summary>
    public int WorkerCount { get; init; } = 1;

    /// <summary>How many due targets one dispatcher pass may claim.</summary>
    public int DispatchBatchSize { get; init; } = 10;

    public int ReconciliationBatchSize { get; init; } = 25;

    /// <summary>
    /// How long a Queued run may sit before the sweep assumes its enqueue was lost. Longer than a
    /// normal queue wait, so an ordinary backlog is not mistaken for a dropped job.
    /// </summary>
    public TimeSpan ReconciliationDelay { get; init; } = TimeSpan.FromMinutes(5);


    /// <summary>
    /// How long a worker's claim on a run stays valid. Comfortably longer than the provider
    /// timeout, so a slow audit is not reclaimed by a second worker while the first is still
    /// waiting on Google.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Total attempts per run, including the first. Bounded because every attempt is a request
    /// against somebody else's quota and against somebody else's site.
    /// </summary>
    public int MaximumAttempts { get; init; } = 3;
}

internal static class PageAuditQueueNames
{
    public const string PageAudits = "page-audits";
}

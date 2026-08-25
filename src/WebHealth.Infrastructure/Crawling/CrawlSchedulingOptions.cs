namespace WebHealth.Infrastructure.Crawling;

public sealed record CrawlSchedulingOptions
{
    public const string SectionName = "Crawling:Scheduling";

    public bool Enabled { get; init; }

    public int WorkerCount { get; init; } = 1;

    public int RequestConcurrency { get; init; } = 2;

    public double RequestsPerSecondPerHost { get; init; } = 2;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(30);

    public int FetchTimeoutSeconds { get; init; } = 15;

    public int TransientRetryCount { get; init; } = 1;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxPageBytes { get; init; } = 1024 * 1024;
}

internal static class CrawlQueueNames
{
    public const string Crawl = "crawl";
}

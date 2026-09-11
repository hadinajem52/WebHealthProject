namespace WebHealth.Infrastructure.PngAudits;

public sealed record PngAuditOptions
{
    public const string SectionName = "PngAudits";

    public bool Enabled { get; init; }

    public int WorkerCount { get; init; } = 1;

    public int MaxPages { get; init; } = 200;

    public int MaxDepth { get; init; } = 5;

    public int MaxPageBytes { get; init; } = 1024 * 1024;

    public long MaxTotalPageBytes { get; init; } = 32L * 1024 * 1024;

    public int MaxImageReferencesPerPage { get; init; } = 1000;

    public int MaxUniqueImages { get; init; } = 500;

    public int MaxTotalImageSourceMappings { get; init; } = 5000;

    public int MaxImageBytes { get; init; } = 8 * 1024 * 1024;

    public long MaxTotalImageBytes { get; init; } = 128L * 1024 * 1024;

    public int MaxWidth { get; init; } = 10000;

    public int MaxHeight { get; init; } = 10000;

    public long MaxDecodedPixels { get; init; } = 30000000;

    public long MaxDecodedMemoryBytes { get; init; } = 256L * 1024 * 1024;

    public int MaxTotalHttpAttempts { get; init; } = 1500;

    public int FetchTimeoutSeconds { get; init; } = 15;

    public double RequestsPerSecondPerHost { get; init; } = 1;

    public int TransientRetryCount { get; init; } = 1;

    public int ImageFetchConcurrency { get; init; } = 1;

    public int ImageDecodeConcurrency { get; init; } = 1;

    public int ComparisonTimeoutSeconds { get; init; } = 60;

    public int MaximumAttempts { get; init; } = 3;

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ReconciliationDelay { get; init; } = TimeSpan.FromMinutes(1);

    public int ReconciliationBatchSize { get; init; } = 100;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(30);

    public decimal MinSavingsPercent { get; init; } = 10;

    public long MinSavingsBytes { get; init; } = 4096;
}

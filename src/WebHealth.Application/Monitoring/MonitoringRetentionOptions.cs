namespace WebHealth.Application.Monitoring;

public sealed class MonitoringRetentionOptions
{
    public const string SectionName = "Monitoring:Retention";
    public bool Enabled { get; init; }
    public bool DryRun { get; init; } = true;
    public int BatchSize { get; init; } = 1000;
    public int MaximumBatchesPerRun { get; init; } = 20;
    public TimeSpan MaximumRunDuration { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (BatchSize is < 1 or > 1000)
            throw new InvalidOperationException("Monitoring retention BatchSize must be between 1 and 1000.");
        if (MaximumBatchesPerRun is < 1 or > 20)
            throw new InvalidOperationException("Monitoring retention MaximumBatchesPerRun must be between 1 and 20.");
        if (MaximumRunDuration < TimeSpan.FromSeconds(1) || MaximumRunDuration > TimeSpan.FromSeconds(30))
            throw new InvalidOperationException("Monitoring retention MaximumRunDuration must be between one and thirty seconds.");
    }
}

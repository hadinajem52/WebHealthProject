namespace WebHealth.Infrastructure.Monitoring;

public sealed class MonitoringSchedulingOptions
{
    public const string SectionName = "Monitoring:Scheduling";

    public bool Enabled { get; init; }
    public int DispatchBatchSize { get; init; } = 50;
    public int RecoveryBatchSize { get; init; } = 100;
    public TimeSpan RecoveryDelay { get; init; } = TimeSpan.FromMinutes(2);
}

internal static class MonitoringQueueNames
{
    public const string ShortChecks = "monitoring";
}

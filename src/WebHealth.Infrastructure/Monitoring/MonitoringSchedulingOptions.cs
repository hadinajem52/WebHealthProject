namespace WebHealth.Infrastructure.Monitoring;

public sealed class MonitoringSchedulingOptions
{
    public const string SectionName = "Monitoring:Scheduling";

    public bool Enabled { get; init; }
    public int DispatchBatchSize { get; init; } = 50;
    public int RecoveryBatchSize { get; init; } = 100;
    public TimeSpan RecoveryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Minimum spacing between urgent certificate checks for one endpoint (BR-C07). Long
    /// enough that a flapping TLS target cannot storm the queue, short enough to observe a
    /// certificate replacement the same day it happens.
    /// </summary>
    public TimeSpan UrgentSslCooldown { get; init; } = TimeSpan.FromHours(1);
}

internal static class MonitoringQueueNames
{
    public const string ShortChecks = "monitoring";
}

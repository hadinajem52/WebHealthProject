namespace WebHealth.Infrastructure.Maintenance;

public sealed class MaintenanceSchedulingOptions
{
    public const string SectionName = "Maintenance:Scheduling";

    public bool Enabled { get; init; }

    /// <summary>
    /// How far ahead recurring windows are materialised. Long enough that a missed expansion tick
    /// cannot leave a recurrence unsuppressed, short enough that an open-ended daily window does
    /// not write years of rows.
    /// </summary>
    public int HorizonDays { get; init; } = 90;

    public int BatchSize { get; init; } = 25;
}

internal static class MaintenanceQueueNames
{
    public const string Maintenance = "maintenance";
}

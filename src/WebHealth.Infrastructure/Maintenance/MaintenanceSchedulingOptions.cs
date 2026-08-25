namespace WebHealth.Infrastructure.Maintenance;

public sealed class MaintenanceSchedulingOptions
{
    public const string SectionName = "Maintenance:Scheduling";

    public bool Enabled { get; init; }

    public int HorizonDays { get; init; } = 90;

    public int BatchSize { get; init; } = 25;
}

internal static class MaintenanceQueueNames
{
    public const string Maintenance = "maintenance";
}

namespace WebHealth.Infrastructure.Maintenance;

public sealed class MaintenanceWindow
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public required string Reason { get; set; }
    public required string TimezoneId { get; set; }
    public required string SuppressionPolicy { get; set; }
    public DateTimeOffset ScheduleStartsAt { get; set; }
    public int ScheduleDurationSeconds { get; set; }
    public required string RecurrencePattern { get; set; }
    public int RecurrenceDaysOfWeek { get; set; }
    public DateTimeOffset? RecurrenceUntil { get; set; }
    public DateTimeOffset? ExpandedThrough { get; set; }
    public bool PauseEscalation { get; set; }
    public bool ContinueFailureCounter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public long Version { get; set; }
    public ICollection<MaintenanceTarget> Targets { get; } = [];
    public ICollection<MaintenanceOccurrence> Occurrences { get; } = [];
}

public sealed class MaintenanceTarget
{
    public Guid Id { get; set; }
    public Guid MaintenanceWindowId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? WebsiteId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public Guid? EndpointId { get; set; }
    public Guid? EndpointMonitorId { get; set; }
    public MaintenanceWindow MaintenanceWindow { get; set; } = null!;
}

public sealed class MaintenanceOccurrence
{
    public Guid Id { get; set; }
    public Guid MaintenanceWindowId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaintenanceWindow MaintenanceWindow { get; set; } = null!;
}

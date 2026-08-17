namespace WebHealth.Infrastructure.Notifications;

public sealed class NotificationSchedulingOptions
{
    public const string SectionName = "Notifications:Scheduling";

    public bool Enabled { get; init; }
    public int DispatchBatchSize { get; init; } = 50;
    public int MaxAttempts { get; init; } = 5;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ReminderInterval { get; init; } = TimeSpan.FromMinutes(60);
    public TimeSpan EscalationDelay { get; init; } = TimeSpan.FromMinutes(30);
}

internal static class NotificationQueueNames
{
    public const string Notifications = "notifications";
}

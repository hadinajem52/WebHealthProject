namespace WebHealth.Domain.Monitoring;

public static class LogicalCheckSources
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";
    public const string Urgent = "Urgent";
}

public static class LogicalCheckStates
{
    public const string Pending = "Pending";
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
}

public static class ExecutionAttemptOutcomes
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string RetryableFailure = "RetryableFailure";
    public const string TerminalFailure = "TerminalFailure";
    public const string Cancelled = "Cancelled";
    public const string Superseded = "Superseded";
}

public static class DurableWorkKinds
{
    public const string HttpCheck = "HttpCheck";
    public const string SslCheck = "SslCheck";
}

public static class DurableWorkStates
{
    public const string Pending = "Pending";
    public const string Dispatching = "Dispatching";
    public const string Enqueued = "Enqueued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class ConfigurationValueSources
{
    public const string SystemDefault = "SystemDefault";
    public const string EnvironmentDefault = "EnvironmentDefault";
    public const string PolicyProfile = "PolicyProfile";
    public const string EndpointOverride = "EndpointOverride";
}

public sealed record MonitorSchedule(DateTimeOffset Anchor, DateTimeOffset NextDueAt);

public static class MonitorCadence
{
    public const short KeyVersion = 1;
    public const int ProductionDefaultIntervalSeconds = 5 * 60;
    public const int NonProductionDefaultIntervalSeconds = 15 * 60;

    public static int GetDefaultIntervalSeconds(bool isProduction) =>
        isProduction ? ProductionDefaultIntervalSeconds : NonProductionDefaultIntervalSeconds;

    public static MonitorSchedule Initialize(DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        return new(utcNow, utcNow);
    }

    public static DateTimeOffset GetFirstSlotAfter(
        DateTimeOffset anchor,
        int intervalSeconds,
        DateTimeOffset instant)
    {
        if (intervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        }

        var utcAnchor = anchor.ToUniversalTime();
        var utcInstant = instant.ToUniversalTime();
        if (utcInstant < utcAnchor)
        {
            return utcAnchor;
        }

        var intervalTicks = TimeSpan.FromSeconds(intervalSeconds).Ticks;
        var elapsedTicks = utcInstant.Ticks - utcAnchor.Ticks;
        var nextSlot = elapsedTicks / intervalTicks + 1;
        return utcAnchor.AddTicks(checked(nextSlot * intervalTicks));
    }

    public static string CreateCadenceKey(Guid endpointMonitorId, DateTimeOffset scheduledFor) =>
        $"v{KeyVersion}|{endpointMonitorId:N}|{scheduledFor.ToUniversalTime().Ticks}";
}

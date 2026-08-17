namespace WebHealth.Domain.Maintenance;

public static class MaintenanceInterval
{
    public static bool Contains(DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset instant) =>
        instant >= startsAt && instant < endsAt;

    public static bool Overlaps(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset otherStartsAt,
        DateTimeOffset otherEndsAt) =>
        startsAt < otherEndsAt && otherStartsAt < endsAt;
}

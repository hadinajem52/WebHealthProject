namespace WebHealth.Domain.Notifications;

public static class NotificationSourceKinds
{
    public const string IncidentEvent = "IncidentEvent";
    public const string Reminder = "Reminder";
    public const string Escalation = "Escalation";
}

public static class NotificationEventTypes
{
    public const string Opened = "Opened";
    public const string Recovered = "Recovered";
    public const string Reminder = "Reminder";
    public const string Escalated = "Escalated";
}

public static class NotificationChannels
{
    public const string Email = "Email";
}

public static class NotificationDeliveryStates
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string RetryScheduled = "RetryScheduled";
    public const string FailedPermanently = "FailedPermanently";
    public const string Suppressed = "Suppressed";
}

public static class NotificationTransportOutcomes
{
    public const string Sent = "Sent";
    public const string TransientFailure = "TransientFailure";
    public const string PermanentFailure = "PermanentFailure";
}

/// <summary>
/// Opening, reminder and escalation keys are deterministic so a duplicate write (retry, restart,
/// competing sweep) collides with the unique (incident_id, source_kind, event_type, occurrence_key)
/// index instead of creating a second notification. Recovery keys off the resolving incident_event
/// so each confirmed recovery still gets its own notification even across recurrence.
/// </summary>
public static class NotificationOccurrenceKeys
{
    public static string Opening(Guid incidentId) => $"v1|opening|{incidentId:N}";

    public static string Recovery(Guid resolvingIncidentEventId) => $"v1|recovery|{resolvingIncidentEventId:N}";

    public static string Reminder(Guid incidentId, int reminderSlot) =>
        $"v1|reminder|{incidentId:N}|{reminderSlot}";

    public static string Escalation(Guid incidentId) => $"v1|escalation|{incidentId:N}";
}

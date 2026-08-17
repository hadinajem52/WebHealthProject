using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Incidents;

public sealed class Incident
{
    public Guid Id { get; set; }
    public Guid EndpointMonitorId { get; set; }
    public Guid OwnerSubjectId { get; set; }
    public Guid? PreviousIncidentId { get; set; }
    public required string IssueKey { get; set; }
    public required string Severity { get; set; }
    public required string Status { get; set; }
    public int RecurrenceCount { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ResolutionCategory { get; set; }
    public string? ResolutionNote { get; set; }
    public long Version { get; set; }
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
    public OwnerSubject OwnerSubject { get; set; } = null!;
    public Incident? PreviousIncident { get; set; }
    public ICollection<IncidentEvent> Events { get; } = [];
    public ICollection<IncidentEvidence> Evidence { get; } = [];
}

public sealed class IncidentEvent
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? ActorUserId { get; set; }
    public long SequenceNumber { get; set; }
    public required string EventType { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public Guid? FromOwnerSubjectId { get; set; }
    public Guid? ToOwnerSubjectId { get; set; }
    public string? BoundedNote { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Incident Incident { get; set; } = null!;
    public ApplicationUser? ActorUser { get; set; }
}

public sealed class IncidentEvidence
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid LogicalCheckId { get; set; }
    public required string EvidenceType { get; set; }
    public required string EvidenceRole { get; set; }
    public required string BoundedSnapshot { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public Incident Incident { get; set; } = null!;
}

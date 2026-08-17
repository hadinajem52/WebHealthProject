using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Monitoring;
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
    public Guid Id { get; init; }
    public Guid IncidentId { get; init; }
    public Guid? ActorUserId { get; init; }
    public long SequenceNumber { get; init; }
    public required string EventType { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public Guid? FromOwnerSubjectId { get; init; }
    public Guid? ToOwnerSubjectId { get; init; }
    public string? BoundedNote { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Incident Incident { get; init; } = null!;
    public ApplicationUser? ActorUser { get; init; }
}

public sealed class IncidentEvidence
{
    public Guid Id { get; init; }
    public Guid IncidentId { get; init; }
    public Guid EndpointMonitorId { get; init; }
    public Guid LogicalCheckId { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceRole { get; init; }
    public required string BoundedSnapshot { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public Incident Incident { get; init; } = null!;
    public LogicalCheck LogicalCheck { get; init; } = null!;
}

using WebHealth.Application.Registry;

namespace WebHealth.Application.Incidents;

public sealed record IncidentListFilter(
    string? Status = null,
    string? Severity = null,
    bool UnacknowledgedOnly = false,
    bool ArchivedOnly = false);

public sealed record IncidentListItem(
    Guid Id,
    string EndpointDisplayUrl,
    string ClientName,
    string WebsiteName,
    string EnvironmentName,
    string IssueKey,
    string Severity,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    string OwnerDisplayName,
    int RecurrenceCount,
    long Version = 0,
    DateTimeOffset? ArchivedAt = null);

public sealed record IncidentListPage(
    IReadOnlyList<IncidentListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int ArchivableCount = 0);

public sealed record IncidentSectionPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IncidentTimelineEntry(
    Guid Id,
    long SequenceNumber,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    string? FromOwnerDisplayName,
    string? ToOwnerDisplayName,
    string? Note,
    string? ActorDisplayName,
    DateTimeOffset OccurredAt);

public sealed record IncidentEvidenceProof(
    string? Severity,
    string? ObservedValue,
    string? ExpectedValue,
    string? SafeDiagnostic,
    int? FailureConfirmationCount);

public sealed record IncidentEvidenceItem(
    Guid Id,
    string EvidenceType,
    string EvidenceRole,
    DateTimeOffset CapturedAt,
    Guid? LogicalCheckId,
    string? ActorDisplayName,
    IncidentEvidenceProof? Proof);

public sealed record IncidentEvidenceSeverityCount(string? Severity, int Count);

public sealed record IncidentEvidenceSampleSummary(
    int Count,
    DateTimeOffset FirstCapturedAt,
    DateTimeOffset LastCapturedAt,
    IReadOnlyList<IncidentEvidenceSeverityCount> Severities);

public sealed record IncidentNotificationDeliveryItem(
    string NormalizedRecipient, string State, int AttemptCount, DateTimeOffset? SentAt);

public sealed record IncidentNotificationItem(
    Guid Id,
    string EventType,
    bool IsSuppressed,
    string? SuppressionReason,
    DateTimeOffset OccurredAt,
    IReadOnlyList<IncidentNotificationDeliveryItem> Deliveries);

public sealed record IncidentDetails(
    Guid Id,
    Guid EndpointMonitorId,
    string EndpointDisplayUrl,
    string ClientName,
    string WebsiteName,
    string EnvironmentName,
    string IssueKey,
    string Severity,
    string Status,
    int RecurrenceCount,
    Guid? PreviousIncidentId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? RecoveryStartedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    long? RecoveryDurationMs,
    long? OutageDurationMs,
    string? ResolutionCategory,
    string? ResolutionNote,
    Guid OwnerSubjectId,
    string OwnerDisplayName,
    long Version,
    bool CanManage,
    IncidentSectionPage<IncidentTimelineEntry> Timeline,
    IReadOnlyList<IncidentEvidenceItem> EvidenceMilestones,
    IncidentSectionPage<IncidentEvidenceItem> FailureSamples,
    IncidentEvidenceSampleSummary? FailureSampleSummary,
    IReadOnlyList<IncidentNotificationItem> Notifications);

public interface IIncidentReader
{
    Task<IncidentListPage> ListAsync(
        IncidentListFilter filter,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default);

    Task<IncidentDetails?> FindAsync(
        Guid incidentId,
        RegistryAccessContext access,
        int timelinePage = 1,
        int samplePage = 1,
        CancellationToken cancellationToken = default);
}

using WebHealth.Application.Registry;

namespace WebHealth.Application.Incidents;

/// <summary>
/// What narrows an incident list. <see cref="ArchivedOnly" /> is a view, not a filter the reader
/// combines with the others: the working list never shows archived incidents and the archive shows
/// nothing else, so the two never have to be reconciled in one query.
/// </summary>
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

/// <summary>
/// <paramref name="ArchivableCount" /> counts every resolved and closed incident the reader can
/// see, ignoring the filters that produced <paramref name="Items" />. The archive sweep is not
/// scoped to the current view, so a count taken through the view's filters would promise to file
/// away fewer incidents than the button actually does.
/// </summary>
public sealed record IncidentListPage(
    IReadOnlyList<IncidentListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int ArchivableCount = 0);

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

public sealed record IncidentEvidenceItem(Guid Id, string EvidenceType, string EvidenceRole, DateTimeOffset CapturedAt);

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
    IReadOnlyList<IncidentTimelineEntry> Timeline,
    IReadOnlyList<IncidentEvidenceItem> Evidence,
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
        CancellationToken cancellationToken = default);
}

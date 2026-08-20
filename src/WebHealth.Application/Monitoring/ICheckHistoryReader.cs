using WebHealth.Application.Registry;

namespace WebHealth.Application.Monitoring;

public interface ICheckHistoryReader
{
    Task<CheckHistoryPage?> ListForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default);

    Task<CheckDetails?> FindCheckAsync(
        Guid logicalCheckId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<CheckHistoryItem?> FindLatestForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Check history with BR-P05 comparability assessed over the results actually shown, so the
/// warning appears on exactly the pages where the mixture exists.
/// </summary>
public sealed record CheckHistoryPage(
    Guid EndpointId,
    string EndpointDisplayUrl,
    IReadOnlyList<CheckHistoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    string? CurrentConfigurationFingerprint,
    ComparabilityAssessment Comparability);

public sealed record CheckHistoryItem(
    Guid LogicalCheckId,
    string Source,
    string State,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? RequestedAt,
    string? InitiatedByDisplayName,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    string? FailureCategory,
    int? HttpStatus,
    int? TotalDurationMs,
    string? MonitorSource,
    bool CountsForUptime)
{
    public IReadOnlyList<KnownIncidentItem> KnownIncidents { get; init; } = [];
}

public sealed record CheckDetails(
    Guid LogicalCheckId,
    Guid EndpointId,
    string EndpointDisplayUrl,
    string Source,
    string State,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? RequestedAt,
    string? InitiatedByDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    string? FailureCategory,
    int? HttpStatus,
    int? TotalDurationMs,
    int? DnsDurationMs,
    int? ConnectDurationMs,
    int? TlsDurationMs,
    int? TtfbDurationMs,
    long? TransferredLength,
    long? DecodedLength,
    string? LengthSource,
    string? MonitorSource,
    DateTimeOffset? MeasuredAt,
    bool ResponseTruncated,
    string? SafeDiagnostic,
    bool CountsForUptime,
    IReadOnlyList<CheckFindingItem> Findings,
    IReadOnlyList<CheckRedirectHopItem> RedirectHops)
{
    public IReadOnlyList<KnownIncidentItem> KnownIncidents { get; init; } = [];
}

public sealed record KnownIncidentItem(
    Guid IncidentId,
    string IssueKey,
    string Status,
    DateTimeOffset AcknowledgedAt);

public sealed record CheckFindingItem(
    string RuleKey,
    string Severity,
    string? ObservedValue,
    string? ExpectedValue,
    string IssueKey);

public sealed record CheckRedirectHopItem(
    int HopNumber,
    string FromUrl,
    string ToUrl,
    int HttpStatus,
    bool IsLoop);

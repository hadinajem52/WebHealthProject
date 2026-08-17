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
}

public sealed record CheckHistoryPage(
    Guid EndpointId,
    string EndpointDisplayUrl,
    IReadOnlyList<CheckHistoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

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
    bool CountsForUptime);

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
    long? DecodedLength,
    bool ResponseTruncated,
    string? SafeDiagnostic,
    bool CountsForUptime,
    IReadOnlyList<CheckFindingItem> Findings,
    IReadOnlyList<CheckRedirectHopItem> RedirectHops);

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

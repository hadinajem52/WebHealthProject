namespace WebHealth.Application.Auditing;

public interface IAuditTrailReader
{
    Task<AuditSearchResult> SearchAsync(
        AuditSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditActor>> ListActorsAsync(CancellationToken cancellationToken = default);
}

public sealed record AuditSearchQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? ActorUserId = null,
    string? Action = null,
    string? Entity = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditSearchResult(
    IReadOnlyList<AuditEventSummary> Events,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record AuditEventSummary(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Action,
    string EntityType,
    string EntityIdentifier,
    string Outcome,
    IReadOnlyDictionary<string, string?> BeforeValues,
    IReadOnlyDictionary<string, string?> AfterValues);

public sealed record AuditActor(Guid UserId, string DisplayName, string Email);

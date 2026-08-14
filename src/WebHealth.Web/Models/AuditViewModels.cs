using WebHealth.Application.Auditing;

namespace WebHealth.Web.Models;

public sealed class AuditIndexViewModel
{
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? Action { get; init; }

    public string? Entity { get; init; }

    public required AuditSearchResult Result { get; init; }

    public required IReadOnlyList<AuditActor> Actors { get; init; }

    public required IReadOnlyList<string> Actions { get; init; }

    public required IReadOnlyList<string> EntityTypes { get; init; }
}

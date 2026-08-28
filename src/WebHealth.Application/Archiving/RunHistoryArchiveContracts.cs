using WebHealth.Application.Registry;

namespace WebHealth.Application.Archiving;

public enum RunHistoryArea
{
    Check,
    Crawl,
    PageAudit,
    PngAudit
}

public enum RunHistoryArchiveStatus
{
    Succeeded,
    Forbidden,
    NotFound,
    NotArchived
}

public sealed record RunHistoryArchiveResult(
    RunHistoryArchiveStatus Status,
    int AffectedCount,
    string? Error)
{
    public bool Succeeded => Status == RunHistoryArchiveStatus.Succeeded;

    public static RunHistoryArchiveResult Success(int affectedCount) =>
        new(RunHistoryArchiveStatus.Succeeded, affectedCount, null);

    public static RunHistoryArchiveResult Failure(RunHistoryArchiveStatus status, string error) =>
        new(status, 0, error);
}

public sealed record RunHistoryScope(
    Guid EndpointId,
    string? Category = null,
    string? Strategy = null);

public interface IRunHistoryArchive
{
    Task<RunHistoryArchiveResult> ArchiveFinishedAsync(
        RunHistoryArea area,
        RunHistoryScope scope,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<RunHistoryArchiveResult> RestoreAsync(
        RunHistoryArea area,
        Guid recordId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

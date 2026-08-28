using WebHealth.Application.Archiving;
using WebHealth.Application.Registry;

namespace WebHealth.IntegrationTests.Support;

internal sealed class EmptyRunHistoryArchive : IRunHistoryArchive
{
    public Task<RunHistoryArchiveResult> ArchiveFinishedAsync(
        RunHistoryArea area,
        RunHistoryScope scope,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RunHistoryArchiveResult.Success(0));

    public Task<RunHistoryArchiveResult> RestoreAsync(
        RunHistoryArea area,
        Guid recordId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RunHistoryArchiveResult.Failure(
            RunHistoryArchiveStatus.NotFound, "The record is not available."));
}

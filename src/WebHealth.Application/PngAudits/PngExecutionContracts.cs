using WebHealth.Application.Registry;

namespace WebHealth.Application.PngAudits;

public sealed record PngAuditManualResult(
    Guid? RunId,
    bool WasAlreadyRunning,
    string? Error,
    EndpointTestBlock Block = EndpointTestBlock.None)
{
    public bool Succeeded => RunId is not null;

    public static PngAuditManualResult Queued(Guid runId) => new(runId, false, null);

    public static PngAuditManualResult AlreadyRunning(Guid runId) => new(runId, true, null);

    public static PngAuditManualResult Rejected(string error) => new(null, false, error);

    public static PngAuditManualResult NotTestable(EndpointTestBlock block) =>
        new(null, false, null, block);
}

public interface IPngAuditRunner
{
    bool CanQueue { get; }

    Task<PngAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public interface IPngAuditRunQueue
{
    void Enqueue(Guid runId);
}

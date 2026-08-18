namespace WebHealth.Application.Monitoring;

/// <summary>
/// Creates an out-of-band certificate check after an availability check failed for a
/// TLS-related reason (BR-C07), so a certificate problem is not left undiagnosed until the next
/// daily slot.
///
/// The request is prepared inside the finalization transaction that records the availability
/// result, so it commits with that result or not at all. Committed work is then recoverable by
/// the normal durable-work reconciliation even if the enqueue never lands — the request cannot
/// be lost by a worker crash between commit and enqueue.
/// </summary>
public interface ISslUrgentCheckScheduler
{
    /// <summary>
    /// Adds the urgent check to the caller's open transaction and returns what to enqueue, or
    /// null when no check is due (not a TLS failure, no certificate monitor, or the per-endpoint
    /// cooldown is still active). The caller must not have saved or committed yet.
    /// </summary>
    Task<UrgentCertificateCheck?> PrepareAfterTlsFailureAsync(
        Guid endpointId,
        LogicalCheckTerminalEvidence evidence,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Enqueues a prepared check. Called only after the transaction commits.</summary>
    Task EnqueueAsync(UrgentCertificateCheck request, CancellationToken cancellationToken = default);
}

public sealed record UrgentCertificateCheck(Guid LogicalCheckId, Guid DurableWorkId);

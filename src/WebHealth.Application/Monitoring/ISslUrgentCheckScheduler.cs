namespace WebHealth.Application.Monitoring;

public interface ISslUrgentCheckScheduler
{
    Task<UrgentCertificateCheck?> PrepareAfterTlsFailureAsync(
        Guid endpointId,
        LogicalCheckTerminalEvidence evidence,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task EnqueueAsync(UrgentCertificateCheck request, CancellationToken cancellationToken = default);
}

public sealed record UrgentCertificateCheck(Guid LogicalCheckId, Guid DurableWorkId);

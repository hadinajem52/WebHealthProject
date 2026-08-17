namespace WebHealth.Application.Notifications;

public sealed record EmailMessage(string ToAddress, string Subject, string TextBody);

public enum EmailTransportOutcome
{
    Sent,
    TransientFailure,
    PermanentFailure
}

public sealed record EmailTransportResult(EmailTransportOutcome Outcome, string? SafeResponse);

/// <summary>
/// Application-owned boundary for outbound email. SMTP delivery must never run inside the
/// finalization transaction that records the check/health/incident/notification state, so
/// callers invoke this only from the separate dispatcher, never from finalization itself.
/// </summary>
public interface IEmailTransport
{
    Task<EmailTransportResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

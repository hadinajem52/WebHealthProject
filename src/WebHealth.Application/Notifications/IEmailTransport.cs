namespace WebHealth.Application.Notifications;

public sealed record EmailMessage(string ToAddress, string Subject, string TextBody);

public enum EmailTransportOutcome
{
    Sent,
    TransientFailure,
    PermanentFailure
}

public sealed record EmailTransportResult(EmailTransportOutcome Outcome, string? SafeResponse);

public interface IEmailTransport
{
    Task<EmailTransportResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

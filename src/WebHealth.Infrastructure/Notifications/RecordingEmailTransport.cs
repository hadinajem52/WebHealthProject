using System.Collections.Concurrent;
using WebHealth.Application.Notifications;

namespace WebHealth.Infrastructure.Notifications;

/// <summary>
/// The default transport for local/demo use and the required completion evidence for this phase.
/// Records every message instead of sending it, so tests and manual verification can inspect what
/// would have been delivered without configuring a real Gmail account.
/// </summary>
public sealed class RecordingEmailTransport : IEmailTransport
{
    private readonly ConcurrentQueue<EmailMessage> sentMessages = new();

    public IReadOnlyCollection<EmailMessage> SentMessages => sentMessages.ToArray();

    public Task<EmailTransportResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        sentMessages.Enqueue(message);
        return Task.FromResult(new EmailTransportResult(EmailTransportOutcome.Sent, "recorded"));
    }
}

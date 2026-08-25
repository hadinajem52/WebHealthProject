using System.Collections.Concurrent;
using WebHealth.Application.Notifications;

namespace WebHealth.Infrastructure.Notifications;

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

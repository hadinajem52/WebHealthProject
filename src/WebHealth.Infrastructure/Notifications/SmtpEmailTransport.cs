using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using WebHealth.Application.Notifications;

namespace WebHealth.Infrastructure.Notifications;

/// <summary>
/// Sends notification email over SMTP with STARTTLS. Invoked only from the dispatcher, never
/// from a finalization transaction. Failures are classified so the dispatcher can retry a
/// transient fault and stop retrying a rejected recipient.
/// </summary>
internal sealed class SmtpEmailTransport(
    SmtpEmailOptions options,
    ILogger<SmtpEmailTransport> logger) : IEmailTransport
{
    public async Task<EmailTransportResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(options.FromDisplayName, options.FromAddress));
        mail.To.Add(MailboxAddress.Parse(message.ToAddress));
        mail.Subject = message.Subject;
        mail.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient
        {
            Timeout = (int)TimeSpan.FromSeconds(options.TimeoutSeconds).TotalMilliseconds
        };

        try
        {
            await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(options.UserName, options.Password, cancellationToken);
            await client.SendAsync(mail, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new(EmailTransportOutcome.Sent, "accepted");
        }
        catch (AuthenticationException exception)
        {
            // Bad credentials will not fix themselves, so do not burn the retry budget.
            logger.LogError(exception, "SMTP authentication failed for host {SmtpHost}.", options.Host);
            return new(EmailTransportOutcome.PermanentFailure, "authentication failed");
        }
        catch (SmtpCommandException exception)
        {
            // RFC 5321: 4xx is a temporary negative reply, 5xx is permanent. Comparing against a
            // single code would retry permanent rejections such as 535 authentication failed.
            var permanent = (int)exception.StatusCode / 100 == 5;
            logger.LogError(
                exception,
                "SMTP command failed with status {SmtpStatus} for host {SmtpHost}.",
                exception.StatusCode,
                options.Host);
            return new(
                permanent ? EmailTransportOutcome.PermanentFailure : EmailTransportOutcome.TransientFailure,
                $"smtp status {(int)exception.StatusCode}");
        }
        // Shutdown is not a delivery failure: let it cancel so no attempt is recorded and the
        // lease simply expires. Only MailKit's own timeout arrives here as a transient fault.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SmtpProtocolException or IOException or OperationCanceledException)
        {
            logger.LogWarning(exception, "SMTP delivery could not complete against host {SmtpHost}.", options.Host);
            return new(EmailTransportOutcome.TransientFailure, "connection failed");
        }
    }
}

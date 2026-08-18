using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Application.Notifications;
using WebHealth.Infrastructure.Notifications;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Sends one real message through the configured SMTP account. Opt-in only: it needs live
/// credentials and delivers to a real mailbox, so it never runs as part of the normal suite.
/// </summary>
public sealed class SmtpEmailTransportTests
{
    [SmtpFact]
    public async Task SendAsync_DeliversToConfiguredRecipient()
    {
        var options = new SmtpEmailOptions
        {
            Enabled = true,
            Host = Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_HOST") ?? "smtp.gmail.com",
            Port = int.TryParse(Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_PORT"), out var port) ? port : 587,
            FromAddress = Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_FROM")!,
            UserName = Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_USER")!,
            Password = Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_PASSWORD")!
        };
        var recipient = Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_TO")!;
        var transport = new SmtpEmailTransport(options, NullLogger<SmtpEmailTransport>.Instance);

        var result = await transport.SendAsync(new EmailMessage(
            recipient,
            "WebHealth test message",
            $"Test message sent from the WebHealth notification transport at {DateTimeOffset.UtcNow:u}."));

        result.Outcome.Should().Be(EmailTransportOutcome.Sent);
    }
}

public sealed class SmtpFactAttribute : FactAttribute
{
    public SmtpFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WEBHEALTH_SMTP_TEST"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set WEBHEALTH_SMTP_TEST=true with live SMTP credentials to send a real message.";
        }
    }
}

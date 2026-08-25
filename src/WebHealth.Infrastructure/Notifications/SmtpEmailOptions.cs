namespace WebHealth.Infrastructure.Notifications;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Notifications:Smtp";

    public bool Enabled { get; init; }
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;

    public string FromAddress { get; init; } = string.Empty;

    public string FromDisplayName { get; init; } = "WebHealth Monitoring";
    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;
}

namespace WebHealth.Infrastructure.Notifications;

/// <summary>
/// Outbound SMTP settings. Disabled by default so local and demo runs keep using
/// <see cref="RecordingEmailTransport" /> and never send real mail by accident.
/// Credentials belong in user secrets or environment variables, never in a checked-in file.
/// </summary>
public sealed class SmtpEmailOptions
{
    public const string SectionName = "Notifications:Smtp";

    public bool Enabled { get; init; }
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;

    /// <summary>Envelope sender. For Gmail this must be the authenticating account.</summary>
    public string FromAddress { get; init; } = string.Empty;

    public string FromDisplayName { get; init; } = "WebHealth Monitoring";
    public string UserName { get; init; } = string.Empty;

    /// <summary>Gmail requires an App Password; an account password will be rejected.</summary>
    public string Password { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;
}

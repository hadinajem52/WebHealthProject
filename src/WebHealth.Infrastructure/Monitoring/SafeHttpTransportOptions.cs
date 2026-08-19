using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed record SafeHttpTransportOptions
{
    public const string SectionName = "Monitoring:HttpTransport";
    public const string ClientName = "MonitoringSafeHttp";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxResponseHeadersKilobytes { get; init; } = 32;
    public int MaxDnsAnswers { get; init; } = 16;
    public int GlobalConcurrency { get; init; } = 20;
    public int PerHostConcurrency { get; init; } = 2;
    public int PerIpConcurrency { get; init; } = 4;
    public string UserAgent { get; init; } = "WebHealthMonitor/1.0";

    /// <summary>
    /// BR-L09. A contact a site owner can reach if our traffic is a problem for them — a URL or a
    /// mailto address. It is part of the shared transport rather than the crawler alone, because
    /// every request this project makes to someone else's host should be traceable back to us.
    /// </summary>
    public string? Contact { get; init; }

    /// <summary>
    /// The header value actually sent: the product token, with the contact as an RFC 9110 comment.
    /// Composed in one place so a crawl and a check cannot identify themselves differently.
    /// </summary>
    public string UserAgentHeader => string.IsNullOrWhiteSpace(Contact)
        ? UserAgent
        : $"{UserAgent} (+{Contact.Trim()})";
}

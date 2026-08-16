using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed record SafeHttpTransportOptions
{
    public const string SectionName = "Monitoring:HttpTransport";
    public const string ClientName = "MonitoringSafeHttp";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxRedirects { get; init; } = SafeHttpTransportDefaults.MaxRedirects;
    public int MaxResponseHeadersKilobytes { get; init; } = 32;
    public int MaxDecodedBodyBytes { get; init; } = SafeHttpTransportDefaults.MaxDecodedBodyBytes;
    public int MaxDnsAnswers { get; init; } = 16;
    public int GlobalConcurrency { get; init; } = 20;
    public int PerHostConcurrency { get; init; } = 2;
    public int PerIpConcurrency { get; init; } = 4;
    public string UserAgent { get; init; } = "WebHealthMonitor/1.0";
}

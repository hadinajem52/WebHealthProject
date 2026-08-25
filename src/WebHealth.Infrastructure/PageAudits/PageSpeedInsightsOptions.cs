namespace WebHealth.Infrastructure.PageAudits;

public sealed record PageSpeedInsightsOptions
{
    public const string SectionName = "PageAudits:PageSpeedInsights";

    public const string ClientName = "PageSpeedInsights";

    public string Locale { get; init; } = "en-US";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public int MaximumResponseBytes { get; init; } = 10 * 1024 * 1024;

    public int MaximumAuditCount { get; init; } = 500;

    public string? ApiKey { get; init; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}

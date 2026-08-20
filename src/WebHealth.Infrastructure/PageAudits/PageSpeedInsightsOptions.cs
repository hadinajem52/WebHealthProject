namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// How this application talks to PageSpeed Insights.
/// </summary>
/// <remarks>
/// The service host and path are deliberately absent. They are constants in
/// <see cref="PageSpeedInsightsProvider" />, so no configuration change can turn this client into
/// a general outbound HTTP client pointed wherever a settings file says.
/// </remarks>
public sealed record PageSpeedInsightsOptions
{
    public const string SectionName = "PageAudits:PageSpeedInsights";

    /// <summary>The client name, so the typed handler is registered and resolved by one string.</summary>
    public const string ClientName = "PageSpeedInsights";

    /// <summary>
    /// Fixed so persisted titles and descriptions stay comparable. A run in one locale and a run
    /// in another produce different stored prose for the same audit, and the comparison would be
    /// reading translations rather than changes.
    /// </summary>
    public string Locale { get; init; } = "en-US";

    /// <summary>
    /// Generous next to a health check, because Google is loading and rendering a page rather
    /// than answering a request. Still bounded: a worker blocked forever is a worker gone.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The cap applied before deserialization, not after. A response is refused on its way in,
    /// so an unexpectedly large payload costs bounded memory rather than whatever Google sent.
    /// </summary>
    public int MaximumResponseBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// A ceiling on how many audits one response may declare. Bounds the work of normalization
    /// against a response that is well-formed and absurd.
    /// </summary>
    public int MaximumAuditCount { get; init; } = 500;

    /// <summary>
    /// Never in configuration that is committed. Read from user secrets or the environment as
    /// <c>PageAudits__PageSpeedInsights__ApiKey</c>.
    /// </summary>
    public string? ApiKey { get; init; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}

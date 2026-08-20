namespace WebHealth.Application.PageAudits;

/// <summary>
/// One audit to ask the provider for. The URL comes from the stored endpoint, never from a caller:
/// a request that accepted an arbitrary URL would let anyone use our API key to audit anything.
/// </summary>
public sealed record PageAuditRequest(
    Uri TargetUrl,
    string Category,
    string Strategy,
    string Locale);

/// <summary>
/// What a provider returned, in this application's vocabulary. No Google type reaches this record,
/// so a second provider is a new adapter rather than a change to everything downstream.
/// </summary>
/// <param name="CategoryScore">
/// Null when the provider produced no trustworthy score. A null here is what separates a run that
/// audited a bad page from a run that failed to audit anything.
/// </param>
public sealed record PageAuditProviderResult(
    string Provider,
    string RequestedUrl,
    string FinalUrl,
    DateTimeOffset AnalysisAt,
    string LighthouseVersion,
    decimal? CategoryScore,
    IReadOnlyList<PageAuditProviderItem> Items,
    IReadOnlyList<string> Warnings,
    string? RuntimeErrorCode,
    string? RuntimeErrorMessage);

/// <summary>One audit inside a category, as the provider described it.</summary>
public sealed record PageAuditProviderItem(
    string AuditId,
    string? Title,
    string? Description,
    decimal? Score,
    string? ScoreDisplayMode,
    double Weight,
    string? Group,
    string? DisplayValue,
    string? Explanation,
    string? ErrorMessage);

/// <summary>
/// A provider failure this application already understands. Thrown rather than returned because
/// every caller of <see cref="IPageAuditProvider" /> has to stop on it, and a result type that
/// could be either would make forgetting to check it the easy mistake.
/// </summary>
/// <remarks>
/// The message is bounded and safe by construction. Google's own error body is never carried here:
/// the request URI it can contain carries the API key.
/// </remarks>
public sealed class PageAuditProviderException(
    string failureCategory,
    string safeDiagnostic,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(safeDiagnostic, innerException)
{
    public string FailureCategory { get; } = failureCategory;

    /// <summary>The provider's own <c>Retry-After</c>, when it sent a usable one.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// The provider seam. One method, because an audit is one request: anything richer here would be
/// modelling Google's API rather than what this feature needs from it.
/// </summary>
public interface IPageAuditProvider
{
    string ProviderName { get; }

    /// <exception cref="PageAuditProviderException">
    /// The audit did not produce a trustworthy result, with a normalized reason.
    /// </exception>
    Task<PageAuditProviderResult> RunAsync(
        PageAuditRequest request,
        CancellationToken cancellationToken = default);
}

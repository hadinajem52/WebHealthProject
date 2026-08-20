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

/// <summary>
/// Where a queued run goes. An interface rather than a direct Hangfire call so the scheduling and
/// execution services stay testable without a background-job server, and so a deployment with the
/// feature switched off fails loudly at the queue rather than silently doing nothing.
/// </summary>
public interface IPageAuditQueue
{
    /// <summary>Queues a run for immediate execution.</summary>
    void Enqueue(Guid runId);

    /// <summary>Queues a run after a delay, for a bounded retry.</summary>
    void Schedule(Guid runId, TimeSpan delay);
}

/// <summary>
/// What one execution attempt decided, so the job can act on it without repeating the reasoning.
/// The retry delay is computed here rather than in the job: whether a failure is worth another
/// attempt is a rule about provider failures, not about Hangfire.
/// </summary>
public sealed record PageAuditExecutionOutcome(
    Guid RunId,
    string Status,
    string? FailureCategory,
    TimeSpan? RetryAfter)
{
    public bool ShouldRetry => RetryAfter is not null;

    /// <summary>Nothing to do: the run was already terminal, or another worker holds it.</summary>
    public static PageAuditExecutionOutcome NotClaimed(Guid runId) => new(runId, "NotClaimed", null, null);
}

/// <summary>
/// What happened to a run somebody asked for by hand. An existing run is a distinct answer from
/// a new one, so the page can say "already running" rather than implying it started something.
/// </summary>
public sealed record PageAuditManualResult(Guid? RunId, bool WasAlreadyRunning, string? Error)
{
    public bool Succeeded => RunId is not null;

    public static PageAuditManualResult Queued(Guid runId) => new(runId, false, null);

    public static PageAuditManualResult AlreadyRunning(Guid runId) => new(runId, true, null);

    public static PageAuditManualResult Rejected(string error) => new(null, false, error);
}

/// <summary>
/// Opening a run on request. The web layer needs this one operation and nothing else the
/// scheduler does, so it is the seam rather than the whole scheduling service - which would drag
/// a database context into every page that renders a button.
/// </summary>
public interface IPageAuditRunner
{
    Task<PageAuditManualResult> QueueManualAsync(
        Guid endpointId,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);
}

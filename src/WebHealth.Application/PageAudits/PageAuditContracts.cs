using WebHealth.Application.Registry;

namespace WebHealth.Application.PageAudits;

public sealed record PageAuditRequest(
    Uri TargetUrl,
    IReadOnlyList<string> Categories,
    string Strategy,
    string Locale);

public sealed record PageAuditProviderBatchResult(
    IReadOnlyDictionary<string, PageAuditProviderResult> Categories);

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
    string? ErrorMessage,
    decimal? NumericValue = null,
    string? NumericUnit = null);

public sealed class PageAuditProviderException(
    string failureCategory,
    string safeDiagnostic,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(safeDiagnostic, innerException)
{
    public string FailureCategory { get; } = failureCategory;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public interface IPageAuditProvider
{
    string ProviderName { get; }

    Task<PageAuditProviderBatchResult> RunAsync(
        PageAuditRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPageAuditQueue
{
    void Enqueue(Guid runId);

    void Schedule(Guid runId, TimeSpan delay);
}

public sealed record PageAuditExecutionOutcome(
    Guid RunId,
    string Status,
    string? FailureCategory,
    TimeSpan? RetryAfter)
{
    public bool ShouldRetry => RetryAfter is not null;

    public static PageAuditExecutionOutcome NotClaimed(Guid runId) => new(runId, "NotClaimed", null, null);
}

public sealed record PageAuditManualResult(
    int QueuedCount,
    int AlreadyRunningCount,
    string? Error,
    EndpointTestBlock Block = EndpointTestBlock.None)
{
    public bool Succeeded => Error is null && Block == EndpointTestBlock.None;

    public bool WasAlreadyRunning => QueuedCount == 0 && AlreadyRunningCount > 0;

    public static PageAuditManualResult Opened(int queuedCount, int alreadyRunningCount) =>
        new(queuedCount, alreadyRunningCount, null);

    public static PageAuditManualResult Rejected(string error) => new(0, 0, error);

    public static PageAuditManualResult NotTestable(EndpointTestBlock block) => new(0, 0, null, block);
}

public interface IPageAuditRunner
{
    Task<PageAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

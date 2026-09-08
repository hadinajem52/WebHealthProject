namespace WebHealth.Application.Monitoring;

public sealed record HttpMonitorOverridesV2
{
    public int SchemaVersion { get; init; } = 2;
    public int? IntervalSeconds { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? FailureConfirmationCount { get; init; }
    public int? RecoveryConfirmationCount { get; init; }
    public int? WarningThresholdMs { get; init; }
    public int? CriticalThresholdMs { get; init; }
    public IReadOnlyList<int>? AdditionalAcceptedStatusCodes { get; init; }
    public string? RequiredContentMarker { get; init; }
    public string? ContentMarkerComparison { get; init; }
}

public sealed record ResolvedHttpMonitoringPolicy(
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureConfirmationCount,
    int RecoveryConfirmationCount,
    int WarningThresholdMs,
    int CriticalThresholdMs,
    IReadOnlyList<int> AdditionalAcceptedStatusCodes,
    string? RequiredContentMarker,
    string ContentMarkerComparison);

public sealed record ResolvedHttpCheckConfiguration(
    ResolvedMonitoringTarget Target,
    ResolvedHttpMonitoringPolicy Policy,
    string ConfigurationFingerprint,
    long CurrentTruthGeneration);

public sealed record HttpMonitoringPolicyResolution(
    ResolvedHttpMonitoringPolicy? Policy,
    IReadOnlyList<ValidationError> Errors)
{
    public bool Succeeded => Policy is not null && Errors.Count == 0;
}

public interface IHttpMonitoringPolicyResolver
{
    HttpMonitoringPolicyResolution Resolve(HttpMonitorOverridesV2 overrides, bool isProduction);
}

public sealed class HttpMonitoringPolicyResolver : IHttpMonitoringPolicyResolver
{
    public HttpMonitoringPolicyResolution Resolve(HttpMonitorOverridesV2 overrides, bool isProduction)
    {
        var errors = new List<ValidationError>();
        var interval = overrides.IntervalSeconds ?? (isProduction ? 300 : 900);
        var timeout = overrides.TimeoutSeconds ?? 15;
        var failures = overrides.FailureConfirmationCount ?? 2;
        var recoveries = overrides.RecoveryConfirmationCount ?? 2;
        var warning = overrides.WarningThresholdMs ?? 1500;
        var critical = overrides.CriticalThresholdMs ?? 3000;
        var statuses = overrides.AdditionalAcceptedStatusCodes?.Distinct().Order().ToArray() ?? [];
        var comparison = overrides.ContentMarkerComparison ?? "OrdinalIgnoreCase";
        if (overrides.SchemaVersion != 2)
            errors.Add(ValidationError.For(nameof(overrides.SchemaVersion), "Unsupported HTTP policy version."));
        ValidateRange(errors, nameof(overrides.IntervalSeconds), interval, 60, 86400);
        ValidateRange(errors, nameof(overrides.TimeoutSeconds), timeout, 1, 120);
        ValidateRange(errors, nameof(overrides.FailureConfirmationCount), failures, 1, 10);
        ValidateRange(errors, nameof(overrides.RecoveryConfirmationCount), recoveries, 1, 10);
        if (warning < 1)
            errors.Add(ValidationError.For(nameof(overrides.WarningThresholdMs), "Warning threshold must be at least 1 millisecond."));
        if (critical < warning || critical > (long)timeout * 1000)
            errors.Add(ValidationError.For(nameof(overrides.CriticalThresholdMs), "Critical threshold must be at least the warning threshold and no greater than the timeout."));
        if (statuses.Length > 20 || statuses.Any(status => status is < 300 or > 499))
            errors.Add(ValidationError.For(nameof(overrides.AdditionalAcceptedStatusCodes), "Choose up to 20 distinct additional status codes between 300 and 499."));
        if (overrides.RequiredContentMarker?.Length > 500)
            errors.Add(ValidationError.For(nameof(overrides.RequiredContentMarker), "Required marker must be 500 characters or fewer."));
        if (comparison is not ("Ordinal" or "OrdinalIgnoreCase"))
            errors.Add(ValidationError.For(nameof(overrides.ContentMarkerComparison), "Choose Ordinal or OrdinalIgnoreCase comparison."));
        return errors.Count > 0
            ? new(null, errors)
            : new(new(interval, timeout, failures, recoveries, warning, critical, statuses,
                string.IsNullOrEmpty(overrides.RequiredContentMarker) ? null : overrides.RequiredContentMarker, comparison), []);
    }

    private static void ValidateRange(List<ValidationError> errors, string field, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
            errors.Add(ValidationError.For(field, $"Enter a value between {minimum} and {maximum}."));
    }
}
